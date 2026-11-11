// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Net.Http.Headers;
using SignalBot.Auth;
using SignalBot.Execution;
using SignalBot.Osm;
using SignalBot.Planning;
using SignalBot.Reporting;
using Signalling.Rules;
using Signalling.Schema;

namespace SignalBot.Core;

// Wires the parts together for one command. The rules of an automated edit
// that do not depend on the data are enforced here, before anything is
// written: the sandbox unless asked otherwise, the dedicated account, the
// approved rule table and the confirmation of the operator. Each command ends
// with the files it wrote and what to check in them.
public sealed class BotApplication(BotSettings settings, CommandLineOptions options, string repositoryRoot)
{
    private readonly string _serverName = options.Server ?? settings.DefaultServer;
    private readonly BotState _state = new(settings, options.Server ?? settings.DefaultServer, repositoryRoot);
    private readonly TokenStore _tokens = new(TokenStore.DefaultPath);
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    private ServerSettings Server => _state.Server;

    private string RulesPath => _state.RulesPath;

    private string RulesHash => _state.RulesHash;

    private string OutputRoot => _state.OutputRoot;

    private string SeededNodesPath => _state.SeededNodesPath;

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

    // Step by step, each one only when needed: the account (kept, or created
    // on the website), the login in the browser, the registration of the
    // application (once per account), the authorization. The account the
    // server reports must be the one given, or nothing is saved.
    private async Task<int> LoginAsync(HttpClient client, OsmApiClient api, CancellationToken stop)
    {
        var site = Server.WebUrl.TrimEnd('/');
        string account;
        if (Server.Account.Length > 0)
        {
            account = ConsolePrompt.Ask("Bot account (Enter to keep it)", Server.Account);
        }
        else
        {
            if (!ConsolePrompt.YesNo($"Does the bot account exist on '{_serverName}'?", defaultValue: true))
            {
                Browser.Open("the sign up page", $"{site}/user/new");
            }

            account = ConsolePrompt.Required("Bot account (display name)");
        }

        // A new account needs its own application.
        var clientId = account == Server.Account ? Server.ClientId : string.Empty;

        Console.WriteLine();
        Browser.Open($"the login page, to log in as '{account}'", $"{site}/login");
        if (clientId.Length == 0)
        {
            clientId = ApplicationRegistration.Ask(_serverName, Server, Banner.ProductName());
        }

        var token = await new OAuthLogin(client, Server).AuthorizeAsync(clientId, stop);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var loggedIn = await api.GetAccountNameAsync(stop);
        if (loggedIn != account)
        {
            Console.WriteLine($"The browser was logged in as '{loggedIn}', not '{account}': nothing saved. Log out, then log in again.");
            return 1;
        }

        _tokens.Save(_serverName, token);
        new LocalSettingsStore(UserProfile.SettingsPath).Set(_serverName, new Dictionary<string, string>
        {
            [LocalSettingsStore.Account] = account,
            [LocalSettingsStore.ClientId] = clientId,
        });

        Console.WriteLine($"Logged in to '{_serverName}' as '{account}'.");
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

        return await ExecuteAsync(api, planner, new ContributorGuard(api, settings.OptOut), groups, mode, limit, settings.Changeset.FixComment, account, source, folder, stop);
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

        if (options.Areas.Count > 0 || options.Mapper is not null || options.InputPath is not null)
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
        var journalPath = options.JournalPath is { } path
            ? Path.GetFullPath(path)
            : _state.LastApplyJournal ?? throw new InvalidOperationException($"No apply run to revert on '{_serverName}'.");
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
        return await ExecuteAsync(api, planner, new ContributorGuard(api, new OptOutSettings()), groups, RunMode.Write, limit, settings.Changeset.RevertComment, account, options, CreateRunFolder(RunMode.Write), stop);
    }

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
            throw new InvalidOperationException($"The rule table changed since it was approved for '{_serverName}': run approve.");
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
            : $"The rule table changed since the approval for '{_serverName}'.");
        Console.WriteLine($"Read {CorrectionListWriter.MarkdownFileName} before approving.");
        if (!Confirm($"approve the rule table sha256 {RulesHash} for '{_serverName}'"))
        {
            return 1;
        }

        var path = UserProfile.CountrySettingsPath(settings.Country.Code);
        new LocalSettingsStore(path).Set(_serverName, new Dictionary<string, string>
        {
            [LocalSettingsStore.ApprovedRulesSha256] = RulesHash,
        });
        Console.WriteLine($"Approved, saved in '{path}'.");
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

    private string DescribeSource(CommandLineOptions source) =>
        options.Command == BotCommand.Revert ? "journal of an earlier run"
        : source.InputPath is { } input ? $"Overpass answer {Path.GetFileName(input)}{(source.Offline ? " (offline)" : string.Empty)}"
        : source.NodesPath is { } nodes ? (nodes == SeededNodesPath ? "test nodes of the last seed" : $"node list {Path.GetFileName(nodes)}")
        : $"Overpass, {(source.Areas.Count > 0 ? $"areas {string.Join(", ", source.Areas)}" : settings.Country.Name)}"
            + (source.Mapper is null ? string.Empty : $", nodes last edited by {source.Mapper}");

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
