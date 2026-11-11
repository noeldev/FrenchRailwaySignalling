// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Osm;
using SignalBot.Reporting;

namespace SignalBot.Planning;

// Restores the tags a run replaced, from its journal. A node is restored only
// when it still carries exactly the tags the run wrote: whatever a mapper
// changed since is kept.
public sealed class RevertPlanner(IReadOnlyDictionary<long, JournalEdit> edits) : IEditPlanner
{
    private static readonly IReadOnlyList<string> Label = ["revert"];

    public EditDecision Decide(OsmNode node)
    {
        if (!node.Visible)
        {
            return EditDecision.Skipped(node, SkipReason.Deleted);
        }

        var edit = edits[node.Id];
        if (TagComparer.AreEqual(node.Tags, edit.Before))
        {
            return EditDecision.Skipped(node, SkipReason.NothingToFix);
        }

        return TagComparer.AreEqual(node.Tags, edit.After)
            ? EditDecision.Edit(node, edit.Before, Label)
            : EditDecision.Skipped(node, SkipReason.ChangedSince);
    }
}
