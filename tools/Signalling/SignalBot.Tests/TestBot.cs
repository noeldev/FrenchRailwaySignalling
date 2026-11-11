// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Execution;
using SignalBot.Osm;
using SignalBot.Planning;
using SignalBot.Reporting;
using Signalling.Rules;
using Signalling.Schema;

namespace SignalBot.Tests;

// A bot wired like the real one, against the fake server, with a small rule
// table: a renamed value, a redundant tag, a fix that needs a complement and
// a guess.
public sealed class TestBot : IDisposable
{
    public const string Area = "Bretagne";

    public static readonly IReadOnlyDictionary<string, string> Legacy = Tags(("railway", "signal"), ("railway:signal:main", "FR:CARRE"));
    public static readonly IReadOnlyDictionary<string, string> Fixed = Tags(("railway", "signal"), ("railway:signal:main", "FR:C"));
    public static readonly IReadOnlyDictionary<string, string> Incomplete = Tags(("railway", "signal"), ("railway:signal:stop", "FR:ARRET_V"));
    public static readonly IReadOnlyDictionary<string, string> Guess = Tags(("railway", "signal"), ("railway:signal:shunting", "FR"));

    public FakeOsmServer Server { get; } = new();

    public LimitSettings Limits { get; } = new()
    {
        MaxEditsPerRun = 100,
        MaxNodesPerChangeset = 100,
        NodesPerRequest = 50,
        ConflictRetries = 2,
        MaxRateLimitWaitSeconds = 5,
    };

    public PolicySettings Policy { get; } = new();

    public OptOutSettings OptOut { get; } = new();

    public TimeSpan QuietPeriod { get; set; } = TimeSpan.Zero;

    public string Folder { get; } = Directory.CreateTempSubdirectory("railwaymapbot-").FullName;

    public string JournalPath => Path.Combine(Folder, RunJournal.FileName);

    public static IReadOnlyDictionary<string, string> Tags(params (string Key, string Value)[] tags) =>
        tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal);

    public FixPlanner CreateFixPlanner()
    {
        var table = new RuleTable
        {
            ValueReplacements = [new() { Key = "railway:signal:main", From = "FR:CARRE", To = "FR:C" }],
            TagReplacements =
            [
                new()
                {
                    Match = { ["railway:signal:stop"] = "FR:ARRET_V" },
                    Set = { ["railway:signal:stop"] = "FR:passenger_stop" },
                    Note = "add railway:signal:stop:conventional:number",
                },
                new()
                {
                    Match = { ["railway:signal:shunting"] = "FR" },
                    Set = { ["railway:signal:shunting"] = "FR:G" },
                    Suggest = true,
                },
            ],
            Redundancies =
            [
                new()
                {
                    Match = { ["railway:crocodile"] = "yes", ["railway:signal:main"] = "FR:C" },
                    Remove = ["railway:crocodile"],
                    Note = "implied by the signal",
                },
            ],
        };

        var schema = new TagSchema(
            new Dictionary<string, KeySpec>
            {
                ["railway:signal:main"] = new("railway:signal:main", new HashSet<string> { "FR:C", "FR:S" }, IsOpen: false),
            },
            "FR");

        var fixer = new SignalFixer(table, schema, "railway:signal:", "FR", Policy.FixMistypedValues);
        return new FixPlanner(fixer, Tags(("railway", "signal")), Policy, OptOut, QuietPeriod, TimeProvider.System);
    }

    // Runs the planner over the nodes as listed (the current ones of the
    // server by default) and returns the summary of the run.
    public async Task<RunSummary> RunAsync(
        RunMode mode,
        IEditPlanner? planner = null,
        IReadOnlyList<OsmNode>? listed = null,
        int limit = int.MaxValue,
        CancellationToken stop = default)
    {
        var summary = new RunSummary();
        var settings = new ChangesetSettings
        {
            Tags = { ["created_by"] = "{tool}", ["bot"] = "yes", ["description"] = "https://wiki.test/Automated_edits/TestBot" },
        };

        using var journal = new RunJournal(JournalPath);
        journal.Write(new JournalRecord("run")
        {
            Run = new RunInfo("test-run", "apply", "dev", FakeOsmServer.Account, "SignalBot/1.0.0", DateTimeOffset.UtcNow, "test", limit, mode != RunMode.Write, "hash"),
        });

        var api = new OsmApiClient(Server.CreateClient(), Limits);
        var runner = new EditRunner(api, planner ?? CreateFixPlanner(), new ContributorGuard(api, planner is null ? OptOut : new OptOutSettings()), journal, summary, new ChangesetTagBuilder(settings), Limits, mode, "Unification ({area}, lot {batch})", Folder);

        IReadOnlyList<OsmNode> nodes = listed ?? [.. Server.Nodes.Values.OrderBy(node => node.Id)];
        await runner.RunAsync([new CandidateGroup(Area, nodes.Count, nodes)], limit, stop);
        return summary;
    }

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
