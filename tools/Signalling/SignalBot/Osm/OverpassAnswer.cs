// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Osm;

// The nodes of an Overpass answer, the name of the area they were listed in
// and the time of the OSM data the answer was computed on, when the answer
// gives them.
public sealed record OverpassAnswer(IReadOnlyList<OsmNode> Nodes, string? AreaName, DateTimeOffset? DataTime = null);
