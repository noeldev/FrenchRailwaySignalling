// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Osm;

// A node as sent to the server: the version it was read at (checked by the
// server, which refuses the upload when the node changed since), its position
// and the whole set of its tags. A node to create has a negative id.
public sealed record NodeWrite(long Id, int Version, double Lat, double Lon, IReadOnlyDictionary<string, string> Tags);

// What the server answers for one uploaded node.
public sealed record NodeResult(long NewId, int NewVersion);
