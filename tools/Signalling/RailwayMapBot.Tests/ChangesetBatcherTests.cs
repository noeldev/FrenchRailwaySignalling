// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;
using RailwayMapBot.Execution;
using RailwayMapBot.Osm;

namespace RailwayMapBot.Tests;

public sealed class ChangesetBatcherTests
{
    [Fact]
    public void Split_keeps_each_batch_within_one_square()
    {
        var limits = new LimitSettings { MaxNodesPerChangeset = 2, MaxChangesetSpanDegrees = 1.0 };
        OsmNode[] nodes =
        [
            Node(1, 48.2, -4.4),
            Node(2, 48.9, -1.1),
            Node(3, 48.3, -4.1),
            Node(4, 48.4, -4.2),
            Node(5, 48.5, -1.5),
        ];

        var batches = ChangesetBatcher.Split(nodes, limits).ToList();

        Assert.Equal([[1, 3], [4], [2, 5]], batches.Select(batch => batch.Select(node => node.Id).ToArray()).ToArray());
    }

    private static OsmNode Node(long id, double lat, double lon) =>
        new(id, 1, true, lat, lon, DateTimeOffset.UnixEpoch, "mapper", TestBot.Legacy);
}
