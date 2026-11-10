// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;
using RailwayMapBot.Osm;

namespace RailwayMapBot.Planning;

// Decides whether a signal gets its fix. Every check that protects the work
// of the mappers is here, and runs on the data just read from the server.
public sealed class FixPlanner(
    SignalFixer fixer,
    IReadOnlyDictionary<string, string> scope,
    PolicySettings policy,
    OptOutSettings optOut,
    TimeSpan quietPeriod,
    TimeProvider clock) : IEditPlanner
{
    private readonly HashSet<string> _optedOutUsers = new(optOut.Users, StringComparer.Ordinal);
    private readonly HashSet<long> _optedOutNodes = [.. optOut.Nodes];

    public EditDecision Decide(OsmNode node)
    {
        if (!node.Visible)
        {
            return EditDecision.Skipped(node, SkipReason.Deleted);
        }

        if (!scope.All(tag => node.Tags.TryGetValue(tag.Key, out var value) && value == tag.Value))
        {
            return EditDecision.Skipped(node, SkipReason.OutOfScope);
        }

        var fix = fixer.Compute(node.Tags);
        if (TagComparer.AreEqual(node.Tags, fix.Tags))
        {
            return EditDecision.Skipped(node, SkipReason.NothingToFix);
        }

        if (_optedOutNodes.Contains(node.Id) || _optedOutUsers.Contains(node.User))
        {
            return EditDecision.Skipped(node, SkipReason.OptedOut, fix.Corrections);
        }

        if ((policy.SkipSuggestions && fix.IsSuggestion) || (policy.SkipIncomplete && fix.NeedsComplement))
        {
            return EditDecision.Skipped(node, SkipReason.NeedsReview, fix.Corrections);
        }

        if (clock.GetUtcNow() - node.Timestamp < quietPeriod)
        {
            return EditDecision.Skipped(node, SkipReason.RecentlyEdited, fix.Corrections);
        }

        return EditDecision.Edit(node, fix.Tags, fix.Corrections);
    }
}
