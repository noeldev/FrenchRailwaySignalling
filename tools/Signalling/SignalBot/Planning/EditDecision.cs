// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Osm;

namespace SignalBot.Planning;

// Why a node is left untouched.
public enum SkipReason
{
    // Nothing to fix (any more): already correct, or fixed by someone else.
    NothingToFix,

    // Deleted since it was listed.
    Deleted,

    // No longer carries the tags of the scope.
    OutOfScope,

    // The node is on the opt-out list.
    OptedOut,

    // Only the signals of the mappers who opted in were asked for, and the
    // last version of this one is by another mapper.
    NotOptedIn,

    // Edited within the quiet period: its mapper may still be working on it.
    RecentlyEdited,

    // The fix is a guess or needs a manual complement: left to a human.
    NeedsReview,

    // Revert only: edited since the run, so its tags are not restored.
    ChangedSince,
}

// What the bot does with one node, decided on its fresh data: replace its
// tags (and why), or skip it (and why).
public sealed record EditDecision(OsmNode Node, IReadOnlyDictionary<string, string>? NewTags, IReadOnlyList<string> Corrections, SkipReason? Skip)
{
    public bool IsEdit => NewTags is not null;

    public static EditDecision Edit(OsmNode node, IReadOnlyDictionary<string, string> tags, IReadOnlyList<string> corrections) =>
        new(node, tags, corrections, null);

    public static EditDecision Skipped(OsmNode node, SkipReason reason, IReadOnlyList<string>? corrections = null) =>
        new(node, null, corrections ?? [], reason);
}

// Decides what to do with a node. The fix and the revert are two planners
// run by the same executor.
public interface IEditPlanner
{
    EditDecision Decide(OsmNode node);
}
