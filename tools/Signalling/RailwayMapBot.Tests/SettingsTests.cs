// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Microsoft.Extensions.Configuration;
using RailwayMapBot.Core;

namespace RailwayMapBot.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void Shipped_settings_are_valid_and_default_to_the_sandbox()
    {
        var settings = Load();

        Assert.Empty(SettingsValidator.Validate(settings));
        Assert.True(settings.Servers[settings.DefaultServer].AllowSeeding);
        Assert.False(settings.Servers["prod"].AllowSeeding);
        Assert.Equal(13, settings.Areas.Count);
    }

    [Fact]
    public void Validation_requires_the_tags_of_an_automated_edit()
    {
        var settings = Load();
        settings.Changeset.Tags.Remove("bot");
        settings.Changeset.Tags.Remove("mechanical");
        settings.Changeset.Tags["description"] = "see the wiki";

        var errors = SettingsValidator.Validate(settings);

        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Validation_reports_every_problem()
    {
        var settings = Load();
        settings.DefaultServer = "nowhere";
        settings.Overpass.Query = ["node[railway=signal];out meta;"];
        settings.Limits.MaxNodesPerChangeset = 20000;
        settings.Servers["prod"].ApiUrl = "http://api.openstreetmap.org";

        var errors = SettingsValidator.Validate(settings);

        Assert.Equal(4, errors.Count);
    }

    private static BotSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build()
            .Get<BotSettings>()!;
}
