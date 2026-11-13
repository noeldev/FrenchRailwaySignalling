// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Osm;

// An OSM node with the metadata of its current version. A deleted node is not
// visible and has no position. Version 0 means the metadata is unknown (an
// export without it), which only an offline plan accepts; so does uid 0.
public sealed record OsmNode(
    long Id,
    int Version,
    bool Visible,
    double Lat,
    double Lon,
    DateTimeOffset Timestamp,
    string User,
    IReadOnlyDictionary<string, string> Tags,
    long Uid = 0);
