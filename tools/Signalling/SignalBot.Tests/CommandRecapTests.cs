// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;

namespace SignalBot.Tests;

// The summary shown before the operator is asked to proceed.
public sealed class CommandRecapTests
{
    private static readonly BotSettings Settings = new()
    {
        Country = new CountrySettings { Code = "FR", Name = "France" },
        Servers =
        {
            ["prod"] = new ServerSettings { Account = "signalbot" },
            ["dev"] = new ServerSettings { Account = "signalbot", AllowSeeding = true },
        },
    };

    private static string[] Describe(BotCommand command, string server, SignalSelection selection, int? limit = null) =>
        CommandRecap.Describe(
            selection.ApplyTo(new CommandLineOptions { Command = command, Server = server, Limit = limit }),
            Settings,
            new BotState(Settings, server, Path.GetTempPath()),
            selection).Split(Environment.NewLine);

    [Fact]
    public void Plan_names_the_server_and_the_signals_and_says_nothing_is_written()
    {
        Assert.Equal(
            new[] { "[plan] on server 'prod'", "  Signals: every signal of France", "  Action:  dry run, nothing is written" },
            Describe(BotCommand.Plan, "prod", SignalSelection.Country));
        Assert.Equal("  Signals: areas BRE, 14", Describe(BotCommand.Plan, "prod", SignalSelection.ForAreas(["BRE", "14"]))[1]);
        Assert.Equal("  Signals: the test nodes of the last seed", Describe(BotCommand.Plan, "dev", SignalSelection.TestNodes)[1]);
    }

    [Fact]
    public void Apply_names_the_account_and_the_largest_number_of_edits()
    {
        Assert.Equal(
            new[] { "[apply] on server 'prod'", "  Signals: last edited by the 2 opted-in mappers (Alice, Bob)", "  Action:  upload at most 200 fixes as 'signalbot'" },
            Describe(BotCommand.Apply, "prod", SignalSelection.ForMappers(["Alice", "Bob"]), 200));
    }

    [Fact]
    public void Seed_and_logout_tell_what_is_done_without_a_signals_line()
    {
        Assert.Equal(
            new[] { "[seed] on server 'dev'", "  Action:  create 50 test signals as 'signalbot', copied from signals of France to fix" },
            Describe(BotCommand.Seed, "dev", SignalSelection.TestNodes, 50));
        Assert.Equal(
            new[] { "[logout] on server 'prod'", "  Action:  revoke the access token of 'signalbot', then forget the account and its client ID on this computer" },
            Describe(BotCommand.Logout, "prod", SignalSelection.Country));
    }

    // 43 mappers must not make a line of 43 names.
    [Fact]
    public void Many_mappers_are_counted_not_named()
    {
        var one = SignalSelection.ForMappers(["Noël Danjou"]);
        var many = SignalSelection.ForMappers([.. Enumerable.Range(1, 43).Select(number => $"mapper {number}")]);

        Assert.Equal("last edited by the opted-in mapper Noël Danjou", one.Describe(Settings));
        Assert.Equal("last edited by the 43 opted-in mappers", many.Describe(Settings));
    }
}
