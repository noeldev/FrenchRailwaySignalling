// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using MapRouletteGen.Osm;
using Signalling.Rules;

namespace MapRouletteGen.Challenge;

// One challenge task: a node with legacy tagging and its fix.
public sealed record LegacyTask(OsmNode Node, TagFix Fix);
