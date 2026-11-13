// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;

namespace SignalBot.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void Parse_reads_an_apply_command()
    {
        var options = CommandLineOptions.Parse(["apply", "--server", "prod", "--area", "BRE,14", "--limit", "50"]);

        Assert.NotNull(options);
        Assert.Equal(BotCommand.Apply, options.Command);
        Assert.Equal("prod", options.Server);
        Assert.Equal(2, options.Areas.Count);
        Assert.Equal(50, options.Limit);
        Assert.True(options.Writes);
    }

    [Fact]
    public void Parse_leaves_the_server_to_the_configured_default()
    {
        var options = CommandLineOptions.Parse(["plan"]);

        Assert.NotNull(options);
        Assert.Null(options.Server);
        Assert.False(options.Writes);
    }

    [Fact]
    public void Parse_leaves_the_journal_and_the_number_of_test_nodes_to_their_defaults()
    {
        var revert = CommandLineOptions.Parse(["revert", "--server", "dev"]);
        var seed = CommandLineOptions.Parse(["seed", "--area", "BRE"]);

        Assert.NotNull(revert);
        Assert.Null(revert.JournalPath);
        Assert.NotNull(seed);
        Assert.Null(seed.Limit);
    }

    [Fact]
    public void Parse_combines_an_area_and_a_mapper()
    {
        var options = CommandLineOptions.Parse(["plan", "--server", "prod", "--country", "FR", "--area", "14", "--mapper", "Noël Danjou"]);

        Assert.NotNull(options);
        Assert.Equal("FR", options.Country);
        Assert.Equal(["14"], options.Areas.ToArray());
        Assert.Equal("Noël Danjou", options.Mapper);
    }

    [Fact]
    public void Parse_rejects_what_makes_no_sense()
    {
        Assert.Null(CommandLineOptions.Parse(["delete"]));
        Assert.Null(CommandLineOptions.Parse(["1"]));
        Assert.Null(CommandLineOptions.Parse(["revert", "--area", "BRE"]));
        Assert.Null(CommandLineOptions.Parse(["rules", "--limit", "5"]));
        Assert.Null(CommandLineOptions.Parse(["plan", "--mapper", "someone", "--input", "export.json"]));
        Assert.Null(CommandLineOptions.Parse(["apply", "--offline", "--input", "export.json"]));
        Assert.Null(CommandLineOptions.Parse(["plan", "--offline"]));
        Assert.Null(CommandLineOptions.Parse(["plan", "--area", "BRE", "--nodes", "ids.txt"]));
        Assert.Null(CommandLineOptions.Parse(["seed", "--nodes", "ids.txt"]));
        Assert.Null(CommandLineOptions.Parse(["apply", "--limit", "0"]));
    }
}
