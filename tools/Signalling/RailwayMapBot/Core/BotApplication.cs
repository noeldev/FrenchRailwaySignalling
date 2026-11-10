// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using RailwayMapBot.Auth;
using RailwayMapBot.Execution;
using RailwayMapBot.Osm;
using RailwayMapBot.Planning;
using RailwayMapBot.Reporting;
using Signalling.Rules;
using Signalling.Schema;

namespace RailwayMapBot.Core;

// Wires the parts together for each command. The rules of an automated edit
// that do not depend on the data are enforced here, before anything is
// written: the sandbox unless asked otherwise, the dedicated account, and the
// confirmation of the operator.
public sealed class BotApplication(BotSettings settings, CommandLineOptions options, string repositoryRoot)
{
    private readonly string _serverName = options.Server ?? settings.DefaultServer;
    private readonly TokenStore _tokens = new(TokenStore.DefaultPath);
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    private ServerSettings Server => settings.Servers.TryGetValue(_serverName, out var server)
        ? server
        : throw new ArgumentException($"Unknown server: {_serverName}. Configured: {string.Join(", ", settings.Servers.Keys)}");

    private string RulesPath => Path.Combine(repositoryRoot, settings.Rules);

    private string RulesHash => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(RulesPath)));

    public async Task<int> RunAsync(CancellationToken stop)
    {
        using var client = CreateClient(Server);
        var api = new OsmApiClient(client, settings.Limits);

        return options.Command switch
        {
            BotCommand.Login => await LoginAsync(client, api, stop),
            BotCommand.Rules => WriteCorrectionList(),
            BotCommand.Seed => await SeedAsync(client, api, stop),
            BotCommand.Revert => await RevertAsync(client, api, stop),
            _ => await FixAsync(client, api, stop),
        };
    }

    private async Task<int> LoginAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(Server.ClientId))
        {
            throw new InvalidOperationException($"servers:{_serverName}:clientId is missing: register the OAuth 2 application first (see the README).");
        }

        var token = await new OAuthLogin(client, Server).AuthorizeAsync(stop);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var account = await api.GetAccountNameAsync(stop);

        _tokens.Save(_serverName, token);
        Console.WriteLine($"Logged in to {_serverName} as '{account}'. Token saved to '{TokenStore.DefaultPath}'.");
        return 0;
    }

    private async Task<int> FixAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        var mode = options.Offline ? RunMode.Offline : options.Command == BotCommand.Apply ? RunMode.Write : RunMode.DryRun;
        var planner = await CreateFixPlannerAsync(client, mode == RunMode.Offline ? TimeSpan.Zero : TimeSpan.FromHours(Server.QuietPeriodHours), stop);
        if (mode == RunMode.Write)
        {
            RequireApprovedRules();
        }

        var account = mode == RunMode.Write ? await AuthenticateAsync(client, api, stop) : string.Empty;

        var groups = Prefilter(await LoadCandidatesAsync(client, api, stop), planner);
        // A dry run shows everything; a real run is bounded unless told otherwise.
        var limit = options.Limit ?? (mode == RunMode.Write ? settings.Limits.MaxEditsPerRun : int.MaxValue);

        Console.WriteLine($"{groups.Sum(group => group.Listed)} nodes listed, {groups.Sum(group => group.Nodes.Count)} to fix or review.");
        if (mode == RunMode.Write && !Confirm($"upload at most {limit} fixes to '{_serverName}' as '{account}'"))
        {
            return 1;
        }

        return await ExecuteAsync(api, planner, new ContributorGuard(api, settings.OptOut), groups, mode, limit, settings.Changeset.FixComment, account, stop);
    }

    private async Task<int> RevertAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        var (run, edits) = RunJournal.ReadUploads(options.JournalPath!);
        if (!run.Server.Equals(_serverName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The journal is a run on '{run.Server}', not on '{_serverName}': use --server {run.Server}.");
        }

        var account = await AuthenticateAsync(client, api, stop);
        Console.WriteLine($"Run {run.RunId}: {edits.Count} uploaded edits.");

        List<CandidateGroup> groups = [];
        foreach (var area in edits.GroupBy(edit => edit.Key, edit => edit.Value.Id))
        {
            var nodes = await api.GetNodesAsync(area, stop);
            groups.Add(new CandidateGroup(area.Key, nodes.Count, nodes));
        }

        var limit = options.Limit ?? int.MaxValue;
        if (!Confirm($"restore the tags of at most {Math.Min(limit, edits.Count)} nodes on '{_serverName}' as '{account}'"))
        {
            return 1;
        }

        var planner = new RevertPlanner(edits.ToDictionary(edit => edit.Value.Id, edit => edit.Value));
        // A revert restores the tags whoever mapped the node: no opt-out applies.
        return await ExecuteAsync(api, planner, new ContributorGuard(api, new OptOutSettings()), groups, RunMode.Write, limit, settings.Changeset.RevertComment, account, stop);
    }

    private async Task<int> SeedAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        if (!Server.AllowSeeding)
        {
            throw new InvalidOperationException($"The server '{_serverName}' does not allow test nodes (allowSeeding): seed is for a sandbox only.");
        }

        var planner = await CreateFixPlannerAsync(client, TimeSpan.Zero, stop);
        var listed = await LoadCandidatesAsync(client, api, stop);
        var nodes = Seeder.Select(listed.SelectMany(group => group.Value).Select(planner.Decide), options.Limit!.Value);
        var account = await AuthenticateAsync(client, api, stop);

        if (nodes.Count == 0)
        {
            Console.WriteLine("No node to fix in the source: nothing to create.");
            return 0;
        }

        if (!Confirm($"create {nodes.Count} test nodes on '{_serverName}' as '{account}'"))
        {
            return 1;
        }

        var ids = await new Seeder(api, new ChangesetTagBuilder(settings.Changeset), settings.Changeset).CreateAsync(nodes, stop);
        var path = Path.Combine(CreateRunFolder(), "seeded-nodes.txt");
        File.WriteAllLines(path, ids.Select(id => id.ToString()));

        Console.WriteLine($"Created {ids.Count} test nodes. Rehearse with:");
        Console.WriteLine($"  RailwayMapBot plan --server {_serverName} --nodes \"{path}\"");
        return 0;
    }

    private async Task<int> ExecuteAsync(
        OsmApiClient api,
        IEditPlanner planner,
        ContributorGuard guard,
        IReadOnlyList<CandidateGroup> groups,
        RunMode mode,
        int limit,
        string commentTemplate,
        string account,
        CancellationToken stop)
    {
        var folder = CreateRunFolder();
        var run = new RunInfo(
            Path.GetFileName(folder),
            options.Command.ToString().ToLowerInvariant(),
            _serverName,
            account,
            Banner.ProductVersion(),
            _started,
            DescribeSource(),
            limit,
            DryRun: mode != RunMode.Write,
            RulesHash);

        var summary = new RunSummary();
        using (var journal = new RunJournal(Path.Combine(folder, RunJournal.FileName)))
        {
            journal.Write(new JournalRecord("run") { Run = run });
            var runner = new EditRunner(api, planner, guard, journal, summary, new ChangesetTagBuilder(settings.Changeset), settings.Limits, mode, commentTemplate, folder);
            await runner.RunAsync(groups, limit, stop);
            journal.Write(new JournalRecord("end") { Outcome = summary.Outcome });
        }

        var reportPath = Path.Combine(folder, RunReport.FileName);
        RunReport.Write(reportPath, run, summary, Server, settings.Changeset.Tags.GetValueOrDefault("description", string.Empty));

        Console.WriteLine();
        Console.WriteLine($"{(run.DryRun ? "Dry run" : "Run")} {summary.Outcome}: {summary.EditCount} nodes {(run.DryRun ? "to edit" : "edited")}, "
            + $"{summary.Skips.Sum(skip => skip.Value)} left untouched, {summary.Changesets.Count} changesets.");
        Console.WriteLine($"Report: '{reportPath}'");
        return summary.Failed ? 1 : 0;
    }

    // The corrections are those the community was shown: another rule table
    // is a change of scope, to document and discuss before it is applied.
    private void RequireApprovedRules()
    {
        var approved = Server.ApprovedRulesSha256;
        if (approved.Length == 0 && Server.AllowSeeding)
        {
            return;
        }

        if (approved.Length == 0)
        {
            throw new InvalidOperationException($"servers:{_serverName}:approvedRulesSha256 is missing: set it to the hash of the rule table once the edit is documented and discussed (current: {RulesHash}).");
        }

        if (!approved.Equals(RulesHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The rule table changed since the edit was discussed (sha256 {RulesHash}): update the documentation, discuss the change, then set servers:{_serverName}:approvedRulesSha256.");
        }
    }

    private int WriteCorrectionList()
    {
        var folder = Path.Combine(repositoryRoot, settings.Output);
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, CorrectionListWriter.FileName);
        CorrectionListWriter.Write(path, RuleTable.Load(RulesPath), settings.Policy, RulesHash);
        Console.WriteLine($"Wrote '{path}' (rule table sha256 {RulesHash}).");
        return 0;
    }

    private async Task<FixPlanner> CreateFixPlannerAsync(HttpClient client, TimeSpan quietPeriod, CancellationToken stop)
    {
        // Only the reviewed local copies of the wiki pages are used, like the
        // published validator rules: the live wiki can change at any time.
        var pages = await new WikiPageReader(client, repositoryRoot).ReadAsync(settings.Wiki, useLocalPages: true, stop);
        var schema = TagSchemaBuilder.Build(pages, settings.KeyPrefix, settings.CountryPrefix);

        Console.WriteLine($"Reading rules from '{RulesPath}'...");
        var fixer = new SignalFixer(RuleTable.Load(RulesPath), schema, settings.KeyPrefix, settings.CountryPrefix, settings.Policy.FixMistypedValues);
        return new FixPlanner(fixer, settings.Scope, settings.Policy, settings.OptOut, quietPeriod, TimeProvider.System);
    }

    private Task<IReadOnlyList<KeyValuePair<string, IReadOnlyList<OsmNode>>>> LoadCandidatesAsync(HttpClient client, OsmApiClient api, CancellationToken stop) =>
        new CandidateLoader(new OverpassClient(client, settings.Overpass, settings.Limits), api, settings).LoadAsync(options, stop);

    // Keeps the nodes the planner has something to say about, so the server
    // is only asked for those.
    private static IReadOnlyList<CandidateGroup> Prefilter(IReadOnlyList<KeyValuePair<string, IReadOnlyList<OsmNode>>> listed, FixPlanner planner) =>
    [
        .. listed.Select(group => new CandidateGroup(
            group.Key,
            group.Value.Count,
            [.. group.Value.Where(node => planner.Decide(node).Skip is not (SkipReason.NothingToFix or SkipReason.OutOfScope or SkipReason.Deleted))])),
    ];

    // Loads the token of the server and checks it belongs to the dedicated
    // account, so the edits are never made under another one.
    private async Task<string> AuthenticateAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(Server.Account))
        {
            throw new InvalidOperationException($"servers:{_serverName}:account is missing: the bot only writes with its dedicated account.");
        }

        var token = _tokens.Find(_serverName)
            ?? throw new InvalidOperationException($"Not logged in to '{_serverName}': run 'RailwayMapBot login --server {_serverName}' first.");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var account = await api.GetAccountNameAsync(stop);
        if (account != Server.Account)
        {
            throw new InvalidOperationException($"Logged in to '{_serverName}' as '{account}', but the bot account is '{Server.Account}'.");
        }

        return account;
    }

    private bool Confirm(string action)
    {
        if (options.AssumeYes)
        {
            return true;
        }

        Console.WriteLine();
        Console.WriteLine($"About to {action}.");
        Console.Write($"Type the server name ({_serverName}) to confirm: ");
        if (Console.ReadLine()?.Trim() == _serverName)
        {
            return true;
        }

        Console.WriteLine("Not confirmed: nothing was written.");
        return false;
    }

    private string CreateRunFolder()
    {
        var folder = Path.Combine(repositoryRoot, settings.Output, $"{_started:yyyyMMdd-HHmmss}-{options.Command.ToString().ToLowerInvariant()}-{_serverName}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string DescribeSource() =>
        options.JournalPath is { } journal ? $"journal {Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(journal)))}"
        : options.InputPath is { } input ? $"export {Path.GetFileName(input)}{(options.Offline ? " (offline)" : string.Empty)}"
        : options.NodesPath is { } nodes ? $"node list {Path.GetFileName(nodes)}"
        : options.Areas.Count > 0 ? $"Overpass, areas {string.Join(", ", options.Areas)}"
        : "Overpass, every configured area";

    // The OSM servers and Overpass ask for an identifiable User-Agent.
    private HttpClient CreateClient(ServerSettings server)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(server.ApiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(settings.Limits.RequestTimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Banner.ProductVersion()} (+{settings.Contact})");
        return client;
    }
}
