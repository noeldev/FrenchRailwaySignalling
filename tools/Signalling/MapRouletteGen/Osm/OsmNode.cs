// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace MapRouletteGen.Osm;

// An OSM node as exported by Overpass: id, position and tags.
public sealed record OsmNode(long Id, double Lat, double Lon, IReadOnlyDictionary<string, string> Tags)
{
    // Element reference used by MapRoulette, for example "node/123".
    public string Reference => $"node/{Id}";
}
