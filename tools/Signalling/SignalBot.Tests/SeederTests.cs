// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Execution;
using SignalBot.Osm;

namespace SignalBot.Tests;

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

        var picked = Seeder.Select(bot.Server.Nodes.Values.OrderBy(node => node.Id).Select(planner.Decide), 2, 1.0);

        Assert.Equal(2, picked.Count);
        Assert.Equal(1, picked[0].Id);
        Assert.Equal(3, picked[1].Id);
    }

    [Fact]
    public void Select_then_spreads_the_nodes_over_the_squares()
    {
        using var bot = new TestBot();
        var planner = bot.CreateFixPlanner();
        // Three signals in one square, one in each of two others.
        var decisions = new[]
        {
            Node(1, 48.1, -4.1), Node(2, 48.2, -4.2), Node(3, 48.3, -4.3),
            Node(4, 47.5, -1.5), Node(5, 43.5, 5.5),
        }.Select(planner.Decide);

        var picked = Seeder.Select(decisions, 4, 1.0);

        // The first node covers the only correction; then one per square in
        // turn (south to north), before a second one in the same square.
        Assert.Equal([1, 5, 4, 2], picked.Select(node => node.Id).ToArray());
    }

    [Fact]
    public async Task Create_keeps_the_positions_with_one_changeset_per_square()
    {
        using var bot = new TestBot();
        var seeder = CreateSeeder(bot);
        // Two signals at both ends of a region, too far apart for one changeset.
        var west = Node(1, 48.0, -4.5);
        var east = Node(2, 48.0, -1.0);

        var ids = await seeder.CreateAsync([west, east], CancellationToken.None);

        var created = ids.Select(id => bot.Server.Nodes[id]).OrderBy(node => node.Lon).ToList();
        Assert.Equal([(48.0, -4.5), (48.0, -1.0)], created.Select(node => (node.Lat, node.Lon)).ToArray());
        Assert.Equal(2, bot.Server.Changesets.Count);
        Assert.True(bot.Server.Changesets.All(changeset => !changeset.IsOpen && changeset.NodeCount == 1));
    }

    [Fact]
    public async Task Create_stops_between_two_changesets()
    {
        using var bot = new TestBot();
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        var ids = await CreateSeeder(bot).CreateAsync([Node(1, 48.0, -4.5)], stop.Token);

        Assert.Empty(ids);
        Assert.Empty(bot.Server.Changesets);
    }

    [Fact]
    public async Task Create_copies_the_nodes_with_their_legacy_tags()
    {
        using var bot = new TestBot();
        var source = new OsmNode(42, 9, true, 47.5, -3.25, DateTimeOffset.UnixEpoch, "mapper", TestBot.Legacy);

        var ids = await CreateSeeder(bot).CreateAsync([source], CancellationToken.None);

        var created = bot.Server.Nodes[Assert.Single(ids)];
        Assert.Equal(1, created.Version);
        Assert.Equal(47.5, created.Lat);
        Assert.Equal("FR:CARRE", created.Tags["railway:signal:main"]);
        Assert.False(Assert.Single(bot.Server.Changesets).IsOpen);
    }

    private static Seeder CreateSeeder(TestBot bot)
    {
        var settings = new ChangesetSettings { SeedComment = "test data", Tags = { ["bot"] = "yes" } };
        return new Seeder(new OsmApiClient(bot.Server.CreateClient(), bot.Limits), new ChangesetTagBuilder(settings), settings, bot.Limits);
    }

    // A signal with a legacy tag.
    private static OsmNode Node(long id, double lat, double lon) =>
        new(id, 1, true, lat, lon, DateTimeOffset.UnixEpoch, "mapper", TestBot.Legacy);
}
