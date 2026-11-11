// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using RailwayMapBot.Auth;
using RailwayMapBot.Execution;
using RailwayMapBot.Osm;
using RailwayMapBot.Planning;
using RailwayMapBot.Reporting;
using Signalling.Rules;
using Signalling.Schema;

namespace RailwayMapBot.Core;

// Wires the parts together for one command. The rules of an automated edit
// that do not depend on the data are enforced here, before anything is
// written: the sandbox unless asked otherwise, the dedicated account, the
// approved rule table and the confirmation of the operator. Each command ends
// with the files it wrote and what to check in them.
public sealed class BotApplication(BotSettings settings, CommandLineOptions options, string repositoryRoot)
{
    private readonly string _serverName = options.Server ?? settings.DefaultServer;
    private readonly TokenStore _tokens = new(TokenStore.DefaultPath);
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    private ServerSettings Server => settings.Servers.TryGetValue(_serverName, out var server)
        ? server
        : throw new ArgumentException($"Unknown server: {_serverName}. Configured: {string.Join(", ", settings.Servers.Keys)}");

    private string RulesPath => Path.GetFullPath(Path.Combine(repositoryRoot, settings.Rules));

    // Hash of the text with LF line ends, so it does not depend on how git
    // checked the file out (CRLF on Windows, LF elsewhere).
    private string RulesHash => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(File.ReadAllText(RulesPath).ReplaceLineEndings("\n"))));

    private string OutputRoot => Path.GetFullPath(Path.Combine(repositoryRoot, settings.Output));

    // Ids of the test nodes of the last seed on the server.
    private string SeededNodesPath => Path.Combine(OutputRoot, $"seeded-nodes-{_serverName}.txt");

    // True when the bot account of the server is known and has a token.
    public static bool IsLoggedIn(BotSettings settings, string serverName) =>
        settings.Servers.TryGetValue(serverName, out var server)
        && server.Account.Length > 0
        && new TokenStore(TokenStore.DefaultPath).Find(serverName) is not null;

    public async Task<int> RunAsync(CancellationToken stop)
    {
        using var client = CreateClient(Server);
        var api = new OsmApiClient(client, settings.Limits);

        return options.Command switch
        {
            BotCommand.Login => await LoginAsync(client, api, stop),
            BotCommand.Rules => WriteCorrectionList(),
            BotCommand.Approve => Approve(),
            BotCommand.Seed => await SeedAsync(client, api, stop),
            BotCommand.Revert => await RevertAsync(client, api, stop),
            _ => await FixAsync(client, api, stop),
        };
    }

    private async Task<int> LoginAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        var clientId = Server.ClientId.Length > 0
            ? Server.ClientId
            : ApplicationRegistration.Ask(_serverName, Server, Banner.ProductName());

        var token = await new OAuthLogin(client, Server).AuthorizeAsync(clientId, stop);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var account = await api.GetAccountNameAsync(stop);

        var expected = Server.Account.Length == 0 || Server.Account == account;
        var question = expected
            ? $"Logged in as '{account}'. Use this account as the bot account on '{_serverName}'?"
            : $"Logged in as '{account}', but the bot account on '{_serverName}' was '{Server.Account}'. Use '{account}' from now on?";
        if (!options.AssumeYes && !ConsolePrompt.YesNo(question, defaultValue: expected))
        {
            Console.WriteLine("Nothing saved: log in again with the bot account.");
            return 1;
        }

        _tokens.Save(_serverName, token);
        new LocalSettingsStore(UserProfile.SettingsPath).Set(_serverName, new Dictionary<string, string>
        {
            [LocalSettingsStore.Account] = account,
            [LocalSettingsStore.ClientId] = clientId,
        });

        Console.WriteLine($"Logged in to '{_serverName}' as '{account}'.");
        Console.WriteLine($"  Account and client ID saved in '{UserProfile.SettingsPath}'.");
        Console.WriteLine($"  Access token saved in '{TokenStore.DefaultPath}'.");
        return 0;
    }

    private async Task<int> FixAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        var mode = options.Offline ? RunMode.Offline : options.Command == BotCommand.Apply ? RunMode.Write : RunMode.DryRun;
        var source = ResolveSource(mode);
        // An unknown area code is reported before anything is read or written.
        _ = new AreaResolver(settings).Resolve(source.Areas);
        var planner = await CreateFixPlannerAsync(client, mode == RunMode.Offline ? TimeSpan.Zero : TimeSpan.FromHours(Server.QuietPeriodHours), stop);
        if (mode == RunMode.Write)
        {
            RequireApprovedRules();
        }

        var account = mode == RunMode.Write ? await AuthenticateAsync(client, api, stop) : string.Empty;

        var folder = CreateRunFolder(mode);
        var groups = Prefilter(await LoadCandidatesAsync(client, api, source, folder, stop), planner);
        // A dry run shows everything; a real run is bounded unless told otherwise.
        var limit = options.Limit ?? (mode == RunMode.Write ? settings.Limits.MaxEditsPerRun : int.MaxValue);

        Console.WriteLine($"{groups.Sum(group => group.Listed)} nodes listed, {groups.Sum(group => group.Nodes.Count)} to fix or review.");
        if (mode == RunMode.Write && !Confirm($"upload at most {limit} fixes to '{_serverName}' as '{account}'"))
        {
            return 1;
        }

        var exitCode = await ExecuteAsync(api, planner, new ContributorGuard(api, settings.OptOut), groups, mode, limit, settings.Changeset.FixComment, account, source, folder, stop);
        PrintNextStep(mode switch
        {
            RunMode.Write when Server.AllowSeeding => "check the changesets on the sandbox (links in report.md); revert restores the test nodes.",
            RunMode.Write => "check the changesets (links in report.md).",
            _ => "read report.md, then open a few changes-NNN.osc in JOSM to check the edits.",
        });
        return exitCode;
    }

    // On a sandbox, the nodes are the test nodes of seed: its database has no
    // copy of the real signals, so the ids listed by Overpass mean nothing
    // there. An offline plan reads no server and takes any source.
    private CommandLineOptions ResolveSource(RunMode mode)
    {
        if (mode == RunMode.Offline || !Server.AllowSeeding || options.NodesPath is not null)
        {
            return options;
        }

        if (options.Areas.Count > 0 || options.InputPath is not null)
        {
            throw new InvalidOperationException($"'{_serverName}' is a sandbox without the real signals: plan and apply work there on the test nodes of seed (or --nodes).");
        }

        if (!File.Exists(SeededNodesPath))
        {
            throw new InvalidOperationException($"No test nodes on '{_serverName}' yet: run seed first.");
        }

        Console.WriteLine($"Using the test nodes of the last seed on '{_serverName}'.");
        return options with { NodesPath = SeededNodesPath };
    }

    private async Task<int> RevertAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        var journalPath = options.JournalPath is { } path ? Path.GetFullPath(path) : FindLastApplyJournal();
        var (run, edits) = RunJournal.ReadUploads(journalPath);
        if (!run.Server.Equals(_serverName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The journal is a run on '{run.Server}', not on '{_serverName}': use --server {run.Server}.");
        }

        var account = await AuthenticateAsync(client, api, stop);
        Console.WriteLine($"Run {run.RunId} ({run.Started.UtcDateTime:yyyy-MM-dd HH:mm} UTC): {edits.Count} uploaded edits.");
        if (edits.Count == 0)
        {
            Console.WriteLine("Nothing to revert.");
            return 0;
        }

        List<CandidateGroup> groups = [];
        foreach (var area in edits.GroupBy(edit => edit.Key, edit => edit.Value.Id))
        {
            var nodes = await api.GetNodesAsync(area, stop);
            groups.Add(new CandidateGroup(area.Key, nodes.Count, nodes));
        }

        var limit = options.Limit ?? int.MaxValue;
        if (!Confirm($"restore the tags of at most {Math.Min(limit, edits.Count)} nodes of run {run.RunId} on '{_serverName}' as '{account}'"))
        {
            return 1;
        }

        var planner = new RevertPlanner(edits.ToDictionary(edit => edit.Value.Id, edit => edit.Value));
        // A revert restores the tags whoever mapped the node: no opt-out applies.
        var exitCode = await ExecuteAsync(api, planner, new ContributorGuard(api, new OptOutSettings()), groups, RunMode.Write, limit, settings.Changeset.RevertComment, account, options, CreateRunFolder(RunMode.Write), stop);
        PrintNextStep("check the changesets (links in report.md).");
        return exitCode;
    }

    // The journal of the last apply run on the server.
    private string FindLastApplyJournal() =>
        (Directory.Exists(OutputRoot)
            ? Directory.GetDirectories(OutputRoot, $"*-{BotCommand.Apply.ToString().ToLowerInvariant()}-{_serverName}")
                .OrderDescending(StringComparer.Ordinal)
                .Select(folder => Path.Combine(folder, RunJournal.FileName))
                .FirstOrDefault(File.Exists)
            : null)
        ?? throw new InvalidOperationException($"No apply run on '{_serverName}' in '{OutputRoot}': nothing to revert.");

    private async Task<int> SeedAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        if (!Server.AllowSeeding)
        {
            throw new InvalidOperationException($"The server '{_serverName}' does not allow test nodes (allowSeeding): seed is for a sandbox only.");
        }

        var account = await AuthenticateAsync(client, api, stop);
        var planner = await CreateFixPlannerAsync(client, TimeSpan.Zero, stop);
        var listed = await LoadCandidatesAsync(client, api, options, saveFolder: null, stop);
        var nodes = Seeder.Select(listed.SelectMany(group => group.Value).Select(planner.Decide), options.Limit ?? Seeder.DefaultCount);

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
        Directory.CreateDirectory(OutputRoot);
        File.WriteAllLines(SeededNodesPath, ids.Select(id => id.ToString()));

        Console.WriteLine($"Created {ids.Count} test nodes, listed in '{SeededNodesPath}'.");
        PrintNextStep($"plan, then apply, on '{_serverName}': they work on these test nodes.");
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
        CommandLineOptions source,
        string folder,
        CancellationToken stop)
    {
        var run = new RunInfo(
            Path.GetFileName(folder),
            options.Command.ToString().ToLowerInvariant(),
            _serverName,
            account,
            Banner.ProductVersion(),
            _started,
            DescribeSource(source),
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

        RunReport.Write(Path.Combine(folder, RunReport.FileName), run, summary, Server, settings.Changeset.Tags.GetValueOrDefault("description", string.Empty));

        Console.WriteLine();
        Console.WriteLine($"{(run.DryRun ? "Dry run" : "Run")} {summary.Outcome}: {summary.EditCount} nodes {(run.DryRun ? "to edit" : "edited")}, "
            + $"{summary.Skips.Sum(skip => skip.Value)} left untouched, {summary.Changesets.Count} changesets.");
        PrintRunFiles(folder, run.DryRun);
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
            throw new InvalidOperationException($"No rule table approved for '{_serverName}': run approve first.");
        }

        if (!approved.Equals(RulesHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The rule table changed since it was approved for '{_serverName}': run rules to see the corrections, then approve.");
        }
    }

    private int WriteCorrectionList()
    {
        Directory.CreateDirectory(OutputRoot);
        var paths = CorrectionListWriter.Write(OutputRoot, RuleTable.Load(RulesPath), settings.Policy, RulesHash);
        Console.WriteLine($"List of the corrections (rule table sha256 {RulesHash}):");
        Console.WriteLine($"  {paths[0]}");
        Console.WriteLine("      the corrections, to read");
        Console.WriteLine($"  {paths[1]}");
        Console.WriteLine("      the same, as wikitext");
        return 0;
    }

    // Records the current rule table as the one apply may use on the server:
    // apply refuses any other table, so the corrections made are always the
    // listed ones.
    private int Approve()
    {
        WriteCorrectionList();
        Console.WriteLine();
        if (Server.ApprovedRulesSha256.Equals(RulesHash, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"This rule table is already approved for '{_serverName}'.");
            return 0;
        }

        Console.WriteLine(Server.ApprovedRulesSha256.Length == 0
            ? $"No rule table is approved for '{_serverName}' yet."
            : $"The rule table approved for '{_serverName}' was sha256 {Server.ApprovedRulesSha256}.");
        if (!Confirm($"approve the rule table sha256 {RulesHash} for '{_serverName}'"))
        {
            return 1;
        }

        new LocalSettingsStore(UserProfile.SettingsPath).Set(_serverName, new Dictionary<string, string>
        {
            [LocalSettingsStore.ApprovedRulesSha256] = RulesHash,
        });
        Console.WriteLine($"Approved, saved in '{UserProfile.SettingsPath}'.");
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

    private Task<IReadOnlyList<KeyValuePair<string, IReadOnlyList<OsmNode>>>> LoadCandidatesAsync(
        HttpClient client, OsmApiClient api, CommandLineOptions source, string? saveFolder, CancellationToken stop) =>
        new CandidateLoader(new OverpassClient(client, settings.Overpass, settings.Limits), api, settings).LoadAsync(source, saveFolder, stop);

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
        var token = _tokens.Find(_serverName);
        if (Server.Account.Length == 0 || token is null)
        {
            throw new InvalidOperationException($"Not logged in to '{_serverName}': run login first.");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var account = await api.GetAccountNameAsync(stop);
        if (account != Server.Account)
        {
            throw new InvalidOperationException($"The token of '{_serverName}' is for '{account}', but the bot account is '{Server.Account}': run login again.");
        }

        return account;
    }

    private bool Confirm(string action) => options.AssumeYes || ConsolePrompt.ConfirmOnServer(action, _serverName);

    // An offline plan reads no server: its folder says so.
    private string CreateRunFolder(RunMode mode)
    {
        var target = mode == RunMode.Offline ? "offline" : _serverName;
        var folder = Path.Combine(OutputRoot, $"{_started:yyyyMMdd-HHmmss}-{options.Command.ToString().ToLowerInvariant()}-{target}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void PrintRunFiles(string folder, bool dryRun)
    {
        Console.WriteLine();
        Console.WriteLine($"Files in '{folder}':");
        Console.WriteLine($"  {RunReport.FileName,-20} what was {(dryRun ? "planned" : "done")}, by tag and by rule: read it first");
        if (Directory.EnumerateFiles(folder, "changes-*.osc").Any())
        {
            Console.WriteLine($"  {"changes-NNN.osc",-20} one per changeset: open them in JOSM to see the edits{(dryRun ? " (never upload them)" : string.Empty)}");
        }

        Console.WriteLine($"  {RunJournal.FileName,-20} every node before and after{(dryRun ? string.Empty : "; revert reads it, keep it")}");
        if (Directory.EnumerateFiles(folder, $"{CandidateLoader.AnswerFilePrefix}*.json").Any())
        {
            Console.WriteLine($"  {CandidateLoader.AnswerFilePrefix + "*.json",-20} data received from Overpass, to plan again offline (--offline --input)");
        }
    }

    private static void PrintNextStep(string step)
    {
        Console.WriteLine();
        Console.WriteLine($"Next: {step}");
    }

    private string DescribeSource(CommandLineOptions source) =>
        options.Command == BotCommand.Revert ? "journal of an earlier run"
        : source.InputPath is { } input ? $"Overpass answer {Path.GetFileName(input)}{(source.Offline ? " (offline)" : string.Empty)}"
        : source.NodesPath is { } nodes ? (nodes == SeededNodesPath ? "test nodes of the last seed" : $"node list {Path.GetFileName(nodes)}")
        : source.Areas.Count > 0 ? $"Overpass, areas {string.Join(", ", source.Areas)}"
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
