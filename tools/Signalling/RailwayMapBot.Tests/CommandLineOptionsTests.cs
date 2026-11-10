// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;

namespace RailwayMapBot.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void Parse_reads_an_apply_command()
    {
        var options = CommandLineOptions.Parse(["apply", "--server", "prod", "--area", "FR-BRE,FR-NOR", "--limit", "50"]);

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
    public void Parse_rejects_what_makes_no_sense()
    {
        Assert.Null(CommandLineOptions.Parse(["delete"]));
        Assert.Null(CommandLineOptions.Parse(["1"]));
        Assert.Null(CommandLineOptions.Parse(["revert"]));
        Assert.Null(CommandLineOptions.Parse(["apply", "--offline", "--input", "export.json"]));
        Assert.Null(CommandLineOptions.Parse(["plan", "--offline"]));
        Assert.Null(CommandLineOptions.Parse(["plan", "--area", "FR-BRE", "--nodes", "ids.txt"]));
        Assert.Null(CommandLineOptions.Parse(["seed", "--input", "export.json"]));
        Assert.Null(CommandLineOptions.Parse(["apply", "--limit", "0"]));
    }
}
