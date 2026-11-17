// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Osm;

namespace SignalBot.Execution;

// Splits the nodes of an area into the batches of the changesets: by squares
// of the grid (limits:maxChangesetSpanDegrees), from the south-west to the
// north-east, then by limits:maxNodesPerChangeset. A changeset therefore
// never covers more than one square, which keeps it under the bounding box
// limit of the server and easy to review on a map.
public static class ChangesetBatcher
{
    public static IEnumerable<OsmNode[]> Split(IReadOnlyList<OsmNode> nodes, LimitSettings limits) =>
        nodes
            .GroupBy(node => Square(node, limits.MaxChangesetSpanDegrees))
            .OrderBy(square => square.Key.Row)
            .ThenBy(square => square.Key.Column)
            .SelectMany(square => square.OrderBy(node => node.Id).Chunk(limits.MaxNodesPerChangeset));

    // The square of the grid a node is in.
    public static (long Row, long Column) Square(OsmNode node, double span) => (Cell(node.Lat, span), Cell(node.Lon, span));

    private static long Cell(double coordinate, double span) => (long)Math.Floor(coordinate / span);
}
