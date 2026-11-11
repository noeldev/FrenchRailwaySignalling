// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;
using RailwayMapBot.Execution;
using RailwayMapBot.Osm;

namespace RailwayMapBot.Tests;

public sealed class SeederTests
{
    [Fact]
    public void Select_covers_the_corrections_first()
    {
        using var bot = new TestBot();
        var planner = bot.CreateFixPlanner();
        bot.Server.Add(1, 1, TestBot.Legacy);
        bot.Server.Add(2, 1, TestBot.Legacy);
        bot.Server.Add(3, 1, TestBot.Tags(("railway", "signal"), ("railway:signal:main", "FR:C"), ("railway:crocodile", "yes")));
        bot.Server.Add(4, 1, TestBot.Fixed);

        var picked = Seeder.Select(bot.Server.Nodes.Values.OrderBy(node => node.Id).Select(planner.Decide), 2);

        Assert.Equal(2, picked.Count);
        Assert.Equal(1, picked[0].Id);
        Assert.Equal(3, picked[1].Id);
    }

    [Fact]
    public async Task Create_copies_the_nodes_with_their_legacy_tags()
    {
        using var bot = new TestBot();
        var settings = new ChangesetSettings { SeedComment = "test data", Tags = { ["bot"] = "yes" } };
        var seeder = new Seeder(new OsmApiClient(bot.Server.CreateClient(), bot.Limits), new ChangesetTagBuilder(settings), settings);
        var source = new OsmNode(42, 9, true, 47.5, -3.25, DateTimeOffset.UnixEpoch, "mapper", TestBot.Legacy);

        var ids = await seeder.CreateAsync([source], CancellationToken.None);

        var created = bot.Server.Nodes[Assert.Single(ids)];
        Assert.Equal(1, created.Version);
        Assert.Equal(47.5, created.Lat);
        Assert.Equal("FR:CARRE", created.Tags["railway:signal:main"]);
        Assert.False(Assert.Single(bot.Server.Changesets).IsOpen);
    }

    [Fact]
    public async Task Create_lays_the_nodes_out_on_a_small_grid()
    {
        using var bot = new TestBot();
        var settings = new ChangesetSettings { SeedComment = "test data", Tags = { ["bot"] = "yes" } };
        var seeder = new Seeder(new OsmApiClient(bot.Server.CreateClient(), bot.Limits), new ChangesetTagBuilder(settings), settings);
        // Two signals at both ends of a region, too far apart for one changeset.
        var west = new OsmNode(1, 1, true, 48.0, -4.5, DateTimeOffset.UnixEpoch, "mapper", TestBot.Legacy);
        var east = new OsmNode(2, 1, true, 48.0, -1.0, DateTimeOffset.UnixEpoch, "mapper", TestBot.Legacy);

        var ids = await seeder.CreateAsync([west, east, west, east, west], CancellationToken.None);

        var created = ids.Select(id => bot.Server.Nodes[id]).ToList();
        Assert.Equal(5, created.Count);
        Assert.True(created.Max(node => node.Lon) - created.Min(node => node.Lon) < 0.01);
        Assert.True(created.Max(node => node.Lat) - created.Min(node => node.Lat) < 0.01);
        Assert.Equal(5, created.Select(node => (node.Lat, node.Lon)).Distinct().Count());
    }
}
