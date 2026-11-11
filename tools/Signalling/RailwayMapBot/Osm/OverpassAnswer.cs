// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Osm;

// The nodes of an Overpass answer, and the name of the area they were listed
// in, when the answer gives it.
public sealed record OverpassAnswer(IReadOnlyList<OsmNode> Nodes, string? AreaName);
