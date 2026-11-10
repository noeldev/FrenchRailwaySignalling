// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Osm;

namespace RailwayMapBot.Planning;

// The nodes of one area (or of one input file) worth reading again, out of
// those listed there. The executor reads them from the server before deciding
// anything.
public sealed record CandidateGroup(string Name, int Listed, IReadOnlyList<OsmNode> Nodes);
