// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Execution;
using SignalBot.Planning;
using SignalBot.Reporting;

namespace SignalBot.Tests;

// The behaviours the automated edits code of conduct asks for, checked end to
// end against the fake server.
public sealed class EditRunnerTests
{
    [Fact]
    public async Task Apply_uploads_the_fix_with_the_version_read_and_the_bot_tags()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 3, TestBot.Legacy);
        bot.Server.Add(2, 7, TestBot.Fixed);

        var summary = await bot.RunAsync(RunMode.Write);

        Assert.Equal("completed", summary.Outcome);
        Assert.Equal(1, summary.EditCount);
        Assert.Equal("FR:C", bot.Server.Nodes[1].Tags["railway:signal:main"]);
        Assert.Equal(4, bot.Server.Nodes[1].Version);
        Assert.Equal(7, bot.Server.Nodes[2].Version);

        var changeset = Assert.Single(bot.Server.Changesets);
        Assert.False(changeset.IsOpen);
        Assert.Equal(1, changeset.NodeCount);
        Assert.Equal("yes", changeset.Tags["bot"]);
        Assert.Equal("Unification (Bretagne, lot 1)", changeset.Tags["comment"]);
        Assert.StartsWith("SignalBot/", changeset.Tags["created_by"]);
        Assert.Equal("https://wiki.test/Automated_edits/TestBot", changeset.Tags["description"]);
    }

    [Fact]
    public async Task Apply_keeps_every_other_tag_and_the_position()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Tags(("railway", "signal"), ("railway:signal:main", "FR:CARRE"), ("ref", "C 12 <a&b>"), ("source", "survey")));
        var before = bot.Server.Nodes[1];

        await bot.RunAsync(RunMode.Write);

        var after = bot.Server.Nodes[1];
        Assert.Equal(4, after.Tags.Count);
        Assert.Equal("C 12 <a&b>", after.Tags["ref"]);
        Assert.Equal("survey", after.Tags["source"]);
        Assert.Equal(before.Lat, after.Lat);
        Assert.Equal(before.Lon, after.Lon);
    }

    [Fact]
    public async Task Apply_twice_changes_nothing_the_second_time()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);

        await bot.RunAsync(RunMode.Write);
        var summary = await bot.RunAsync(RunMode.Write);

        Assert.Equal(0, summary.EditCount);
        Assert.Single(bot.Server.Changesets);
        Assert.Equal(2, bot.Server.Nodes[1].Version);
    }

    [Fact]
    public async Task Apply_decides_on_the_fresh_data_not_on_the_listing()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);
        var listed = bot.Server.Nodes.Values.ToList();

        // A mapper fixes the signal after it was listed.
        bot.Server.EditAs("mapper", 1, TestBot.Fixed);
        var summary = await bot.RunAsync(RunMode.Write, listed: listed);

        Assert.Equal(0, summary.EditCount);
        Assert.Equal(1, summary.Skips[SkipReason.NothingToFix]);
        Assert.Equal(0, bot.Server.Writes);
        Assert.Equal("mapper", bot.Server.Nodes[1].User);
    }

    [Fact]
    public async Task Apply_never_forces_a_conflict_it_reads_and_decides_again()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);
        bot.Server.Add(2, 1, TestBot.Legacy);

        // A mapper edits node 1 between the read and the upload.
        var mapperTags = TestBot.Tags(("railway", "signal"), ("railway:signal:main", "FR:S"), ("note", "checked on site"));
        bot.Server.BeforeNextUpload = () => bot.Server.EditAs("mapper", 1, mapperTags);

        var summary = await bot.RunAsync(RunMode.Write);

        Assert.Equal("completed", summary.Outcome);
        Assert.Equal(1, summary.EditCount);
        Assert.Equal("mapper", bot.Server.Nodes[1].User);
        Assert.Equal("checked on site", bot.Server.Nodes[1].Tags["note"]);
        Assert.Equal(2, bot.Server.Nodes[1].Version);
        Assert.Equal("FR:C", bot.Server.Nodes[2].Tags["railway:signal:main"]);
        Assert.False(Assert.Single(bot.Server.Changesets).IsOpen);
    }

    [Fact]
    public async Task Apply_stops_at_the_edit_limit()
    {
        using var bot = new TestBot();
        bot.Limits.MaxNodesPerChangeset = 2;
        for (var id = 1; id <= 5; id++)
        {
            bot.Server.Add(id, 1, TestBot.Legacy);
        }

        var summary = await bot.RunAsync(RunMode.Write, limit: 3);

        Assert.Equal(3, summary.EditCount);
        Assert.Equal("stopped: edit limit reached", summary.Outcome);
        Assert.Equal(3, bot.Server.Nodes.Values.Count(node => node.Version == 2));
        Assert.Equal(2, bot.Server.Changesets.Count);
    }

    [Fact]
    public async Task Dry_run_reads_the_server_and_writes_nothing()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);

        var summary = await bot.RunAsync(RunMode.DryRun);

        Assert.Equal(1, summary.EditCount);
        Assert.Equal(0, bot.Server.Writes);
        Assert.True(bot.Server.NodeReads > 0);
        Assert.Null(Assert.Single(summary.Changesets).Id);
        Assert.True(File.Exists(Path.Combine(bot.Folder, "changes-001.osc")));
    }

    [Fact]
    public async Task Apply_leaves_guesses_and_incomplete_fixes_to_a_human()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Incomplete);
        bot.Server.Add(2, 1, TestBot.Guess);

        var summary = await bot.RunAsync(RunMode.Write);

        Assert.Equal(2, summary.Skips[SkipReason.NeedsReview]);
        Assert.Equal(0, bot.Server.Writes);
    }

    [Fact]
    public async Task Apply_removes_a_redundant_tag_although_its_rule_has_a_note()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Tags(("railway", "signal"), ("railway:signal:main", "FR:C"), ("railway:crocodile", "yes")));

        await bot.RunAsync(RunMode.Write);

        Assert.False(bot.Server.Nodes[1].Tags.ContainsKey("railway:crocodile"));
    }

    [Fact]
    public async Task Apply_skips_deleted_opted_out_recent_and_out_of_scope_nodes()
    {
        using var bot = new TestBot { QuietPeriod = TimeSpan.FromHours(24) };
        bot.OptOut.Nodes.Add(2);
        bot.OptOut.Nodes.Add(4);
        bot.Server.Add(1, 1, TestBot.Legacy);
        bot.Server.Add(2, 1, TestBot.Legacy);
        bot.Server.Add(3, 1, TestBot.Legacy, timestamp: DateTimeOffset.UtcNow.AddHours(-1));
        bot.Server.Add(4, 1, TestBot.Legacy);
        bot.Server.Add(5, 1, TestBot.Tags(("railway", "milestone"), ("railway:signal:main", "FR:CARRE")));
        bot.Server.Add(6, 1, TestBot.Legacy);
        bot.Server.Delete(1);

        var summary = await bot.RunAsync(RunMode.Write);

        Assert.Equal(1, summary.EditCount);
        Assert.Equal(1, summary.Skips[SkipReason.Deleted]);
        Assert.Equal(2, summary.Skips[SkipReason.OptedOut]);
        Assert.Equal(1, summary.Skips[SkipReason.RecentlyEdited]);
        Assert.Equal(1, summary.Skips[SkipReason.OutOfScope]);
        Assert.Equal(2, bot.Server.Nodes[6].Version);
    }

    [Fact]
    public async Task Apply_fixes_only_the_signals_last_edited_by_an_opted_in_mapper()
    {
        using var bot = new TestBot { OptedIn = new HashSet<long> { FakeOsmServer.UidOf("willing mapper") } };
        bot.Server.Add(1, 1, TestBot.Legacy, user: "willing mapper");
        bot.Server.Add(2, 1, TestBot.Legacy, user: "willing mapper");
        bot.Server.EditAs("someone else", 2, TestBot.Legacy);
        bot.Server.Add(3, 1, TestBot.Legacy, user: "someone else");
        bot.Server.EditAs("willing mapper", 3, TestBot.Legacy);

        var summary = await bot.RunAsync(RunMode.Write);

        Assert.Equal(2, summary.EditCount);
        Assert.Equal(1, summary.Skips[SkipReason.NotOptedIn]);
        Assert.Equal("FR:CARRE", bot.Server.Nodes[2].Tags["railway:signal:main"]);
        Assert.Equal("FR:C", bot.Server.Nodes[3].Tags["railway:signal:main"]);
    }

    [Fact]
    public async Task Apply_waits_when_the_server_limits_the_rate_for_a_short_time()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);
        bot.Server.RateLimitNextUpload = 0;

        var summary = await bot.RunAsync(RunMode.Write);

        Assert.Equal(1, summary.EditCount);
        Assert.Equal(2, bot.Server.Nodes[1].Version);
    }

    [Fact]
    public async Task Apply_stops_and_closes_the_changeset_when_the_wait_is_too_long()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);
        bot.Server.RateLimitNextUpload = 3600;

        var summary = await bot.RunAsync(RunMode.Write);

        Assert.True(summary.Failed);
        Assert.Equal(0, summary.EditCount);
        Assert.Equal(1, bot.Server.Nodes[1].Version);
        Assert.False(Assert.Single(bot.Server.Changesets).IsOpen);
    }

    [Fact]
    public async Task Apply_does_nothing_once_the_operator_asked_to_stop()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var summary = await bot.RunAsync(RunMode.Write, stop: stop.Token);

        Assert.Equal("stopped by the operator", summary.Outcome);
        Assert.Equal(0, bot.Server.Writes);
    }

    [Fact]
    public async Task Journal_holds_the_tags_before_and_after_every_uploaded_edit()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 3, TestBot.Legacy);

        await bot.RunAsync(RunMode.Write);
        var (run, edits) = RunJournal.ReadUploads(bot.JournalPath);

        var edit = Assert.Single(edits);
        Assert.Equal("test-run", run.RunId);
        Assert.Equal(TestBot.Area, edit.Key);
        Assert.Equal(3, edit.Value.Version);
        Assert.Equal("mapper", edit.Value.User);
        Assert.Equal("FR:CARRE", edit.Value.Before["railway:signal:main"]);
        Assert.Equal("FR:C", edit.Value.After["railway:signal:main"]);
        Assert.Equal("railway:signal:main=FR:CARRE", Assert.Single(edit.Value.Corrections));
    }

    // An editor that takes the journal for one JSON document may reformat it:
    // it must still be readable, since revert depends on it.
    [Fact]
    public async Task Journal_is_read_whatever_its_line_breaks()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 3, TestBot.Legacy);
        await bot.RunAsync(RunMode.Write);

        var records = File.ReadAllLines(bot.JournalPath)
            .Select(line => System.Text.Json.Nodes.JsonNode.Parse(line)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(bot.JournalPath, string.Join(" ", records), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var (run, edits) = RunJournal.ReadUploads(bot.JournalPath);

        Assert.Equal("test-run", run.RunId);
        Assert.Equal("FR:C", Assert.Single(edits).Value.After["railway:signal:main"]);
    }

    [Fact]
    public async Task Revert_restores_only_the_nodes_nobody_edited_since()
    {
        using var bot = new TestBot();
        bot.Server.Add(1, 1, TestBot.Legacy);
        bot.Server.Add(2, 1, TestBot.Legacy);
        await bot.RunAsync(RunMode.Write);
        var (_, edits) = RunJournal.ReadUploads(bot.JournalPath);

        // A mapper improves node 2 after the run.
        var mapperTags = TestBot.Tags(("railway", "signal"), ("railway:signal:main", "FR:C"), ("ref", "C 5"));
        bot.Server.EditAs("mapper", 2, mapperTags);

        var planner = new RevertPlanner(edits.ToDictionary(edit => edit.Value.Id, edit => edit.Value));
        var summary = await bot.RunAsync(RunMode.Write, planner);

        Assert.Equal(1, summary.EditCount);
        Assert.Equal(1, summary.Skips[SkipReason.ChangedSince]);
        Assert.Equal("FR:CARRE", bot.Server.Nodes[1].Tags["railway:signal:main"]);
        Assert.Equal("C 5", bot.Server.Nodes[2].Tags["ref"]);
        Assert.Equal("mapper", bot.Server.Nodes[2].User);
    }
}
