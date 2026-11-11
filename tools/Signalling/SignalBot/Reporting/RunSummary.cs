// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Planning;

namespace SignalBot.Reporting;

// The figures of a run, gathered while it runs, for the report.
public sealed class RunSummary
{
    public List<AreaSummary> Areas { get; } = [];

    public List<ChangesetSummary> Changesets { get; } = [];

    // Nodes per tag change (see TagChanges), among the edited ones.
    public Dictionary<string, int> TagChanges { get; } = new(StringComparer.Ordinal);

    // Nodes per correction, among the edited ones.
    public Dictionary<string, int> Corrections { get; } = new(StringComparer.Ordinal);

    // Nodes per correction, among those left to a human.
    public Dictionary<string, int> ReviewCorrections { get; } = new(StringComparer.Ordinal);

    public Dictionary<SkipReason, int> Skips { get; } = [];

    // Edited nodes per last mapper.
    public Dictionary<string, int> Mappers { get; } = new(StringComparer.Ordinal);

    public int EditCount => Changesets.Where(changeset => changeset.Done).Sum(changeset => changeset.Edits);

    public string Outcome { get; set; } = "interrupted";

    public bool Failed { get; set; }

    public void AddEdit(EditDecision decision)
    {
        Areas[^1].Edits++;
        Count(Mappers, decision.Node.User);
        foreach (var change in Reporting.TagChanges.Describe(decision.Node.Tags, decision.NewTags!))
        {
            Count(TagChanges, change);
        }

        foreach (var correction in decision.Corrections)
        {
            Count(Corrections, correction);
        }
    }

    public void AddSkip(EditDecision decision)
    {
        var reason = decision.Skip!.Value;
        Skips[reason] = Skips.GetValueOrDefault(reason) + 1;
        if (reason == SkipReason.NeedsReview)
        {
            foreach (var correction in decision.Corrections)
            {
                Count(ReviewCorrections, correction);
            }
        }
    }

    private static void Count(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
}

// One area: the nodes listed in it, those with something to fix and those
// edited.
public sealed class AreaSummary(string name, int listed, int candidates)
{
    public string Name { get; } = name;

    public int Listed { get; } = listed;

    public int Candidates { get; } = candidates;

    public int Edits { get; set; }
}

// One batch. Id is null in a dry run; Done is false when the upload failed or
// when nothing was left to upload after a conflict.
public sealed record ChangesetSummary(int Batch, string Area, long? Id, int Edits, bool Done);
