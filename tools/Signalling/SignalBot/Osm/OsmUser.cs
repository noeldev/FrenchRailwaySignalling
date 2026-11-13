// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Osm;

// An OSM account: its id never changes, its display name can.
public sealed record OsmUser(long Uid, string Name)
{
    public override string ToString() => $"{Name} (uid {Uid})";
}
