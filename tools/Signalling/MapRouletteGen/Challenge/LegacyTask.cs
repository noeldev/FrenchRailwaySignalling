// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using MapRouletteGen.Osm;
using Signalling.Rules;
using Signalling.Schema;

namespace MapRouletteGen.Challenge;

// One challenge task: a node with legacy tagging or mistyped values, and its
// fix (rule table first, then the corrections of the mistyped values).
public sealed record LegacyTask(OsmNode Node, TagFix Fix, IReadOnlyList<TokenFix> TokenFixes)
{
    // What was found on the node, as "key=value", for the instruction.
    public IEnumerable<string> Findings =>
        Fix.AppliedRules.Concat(TokenFixes.Select(fix => $"{fix.Key}={fix.From}")).Distinct(StringComparer.Ordinal);
}
