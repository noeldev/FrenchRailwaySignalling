// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Reporting;

namespace SignalBot.Tests;

// Which apply run revert undoes: the newest one of the same server that
// uploaded edits and is not reverted yet. A run on one server is never
// reverted from another.
public sealed class BotStateTests : IDisposable
{
    private static readonly BotSettings Settings = new()
    {
        Country = new CountrySettings { Code = "FR", Name = "France" },
        Output = "runs",
        Contact = "https://example.test",
        Limits = new LimitSettings { RequestTimeoutSeconds = 5 },
        Servers =
        {
            ["prod"] = new ServerSettings { ApiUrl = "https://prod.test", Account = "signalbot" },
            ["dev"] = new ServerSettings { ApiUrl = "https://dev.test", Account = "signalbot", AllowSeeding = true },
        },
    };

    private readonly string _root = Directory.CreateTempSubdirectory("signalbot-state-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Only_the_runs_of_the_server_count()
    {
        var devApply = WriteRun("20261117-100000-apply-dev");
        WriteRun("20261117-110000-apply-prod");
        WriteRun("20261117-120000-revert-prod", reverts: "20261117-110000-apply-prod");

        Assert.Equal(devApply, State("dev").LastApplyJournal);
        Assert.Null(State("prod").LastApplyJournal);
    }

    [Fact]
    public void Reverts_undo_the_apply_runs_from_the_newest_to_the_oldest()
    {
        var first = WriteRun("20261117-100000-apply-dev");
        var second = WriteRun("20261117-110000-apply-dev");
        Assert.Equal(second, State("dev").LastApplyJournal);

        WriteRun("20261117-120000-revert-dev", reverts: "20261117-110000-apply-dev");
        Assert.Equal(first, State("dev").LastApplyJournal);

        WriteRun("20261117-130000-revert-dev", reverts: "20261117-100000-apply-dev");
        Assert.Null(State("dev").LastApplyJournal);
    }

    [Fact]
    public void An_apply_run_without_upload_has_nothing_to_revert()
    {
        var uploaded = WriteRun("20261117-100000-apply-dev");
        WriteRun("20261117-110000-apply-dev", uploads: false);

        Assert.Equal(uploaded, State("dev").LastApplyJournal);
    }

    // Revert journals written before they named the run they undo.
    [Fact]
    public void A_revert_without_the_name_of_its_run_undid_the_apply_run_just_before_it()
    {
        var first = WriteRun("20261117-100000-apply-dev");
        WriteRun("20261117-110000-apply-dev");
        WriteRun("20261117-120000-revert-dev");

        Assert.Equal(first, State("dev").LastApplyJournal);
    }

    [Fact]
    public async Task Revert_refuses_the_journal_of_another_server()
    {
        var prodApply = WriteRun("20261117-100000-apply-prod");
        var options = new CommandLineOptions { Command = BotCommand.Revert, Server = "dev", JournalPath = prodApply };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new BotApplication(Settings, options, _root).RunAsync(CancellationToken.None));

        Assert.Contains("run on 'prod', not on 'dev'", error.Message);
    }

    private BotState State(string server) => new(Settings, server, _root);

    // A run folder with its journal: the run record, then one uploaded batch
    // unless uploads is false. Returns the path of the journal.
    private string WriteRun(string runId, string? reverts = null, bool uploads = true)
    {
        var parts = runId.Split('-');
        var folder = Path.Combine(_root, Settings.Output, Settings.Country.Code, runId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, RunJournal.FileName);
        using var journal = new RunJournal(path);
        journal.Write(new JournalRecord("run")
        {
            Run = new RunInfo(runId, parts[2], parts[3], "signalbot", "SignalBot/1.0.0", DateTimeOffset.UnixEpoch, "test", 10, false, "hash", reverts),
        });
        if (uploads)
        {
            var edit = new JournalEdit(1, 1, 48.0, -4.0, "mapper", DateTimeOffset.UnixEpoch, TestBot.Legacy, TestBot.Fixed, ["railway:signal:main=FR:CARRE"]);
            journal.Write(new JournalRecord("batch") { Batch = 1, Area = TestBot.Area, Changeset = 7, Edits = [edit] });
        }

        return path;
    }
}
