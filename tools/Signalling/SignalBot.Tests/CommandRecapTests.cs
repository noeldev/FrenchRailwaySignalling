// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;

namespace SignalBot.Tests;

// The sentence shown before the operator is asked to proceed.
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

    private static string Describe(BotCommand command, string server, SignalSelection selection, int? limit = null) =>
        CommandRecap.Describe(
            selection.ApplyTo(new CommandLineOptions { Command = command, Server = server, Limit = limit }),
            Settings,
            new BotState(Settings, server, Path.GetTempPath()),
            selection);

    [Fact]
    public void Plan_names_the_server_and_the_signals_and_says_nothing_is_written()
    {
        Assert.Equal(
            "[plan] on server 'prod' for every signal of France: dry run, nothing is written.",
            Describe(BotCommand.Plan, "prod", SignalSelection.Country));
        Assert.Equal(
            "[plan] on server 'prod' for the signals of areas BRE, 14: dry run, nothing is written.",
            Describe(BotCommand.Plan, "prod", SignalSelection.ForAreas(["BRE", "14"])));
        Assert.Equal(
            "[plan] on server 'dev' for the test nodes of the last seed: dry run, nothing is written.",
            Describe(BotCommand.Plan, "dev", SignalSelection.TestNodes));
    }

    [Fact]
    public void Apply_names_the_account_and_the_largest_number_of_edits()
    {
        Assert.Equal(
            "[apply] on server 'prod' for the signals last edited by the 2 opted-in mappers (Alice, Bob): upload at most 200 fixes as 'signalbot'.",
            Describe(BotCommand.Apply, "prod", SignalSelection.ForMappers(["Alice", "Bob"]), 200));
    }

    [Fact]
    public void Seed_tells_what_is_created()
    {
        Assert.Equal(
            "[seed] on server 'dev': create 50 test signals as 'signalbot', copied from signals of France to fix.",
            Describe(BotCommand.Seed, "dev", SignalSelection.TestNodes, 50));
    }

    [Fact]
    public void A_long_list_of_mappers_is_shortened()
    {
        var selection = SignalSelection.ForMappers(["A", "B", "C", "D", "E", "F", "G"]);

        Assert.Equal("the signals last edited by the 7 opted-in mappers (A, B, C, D, E and 2 more)", selection.Describe(Settings));
    }
}
