// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Reporting;

// What identifies a run, written first in its journal and at the top of its
// report. The hash tells which rule table took the decisions.
public sealed record RunInfo(
    string RunId,
    string Command,
    string Server,
    string Account,
    string Tool,
    DateTimeOffset Started,
    string Source,
    int EditLimit,
    bool DryRun,
    string RulesSha256);

// One edited node: the version it was read at, its last mapper, and its tags
// before and after. This is all a revert needs.
public sealed record JournalEdit(
    long Id,
    int Version,
    double Lat,
    double Lon,
    string User,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, string> Before,
    IReadOnlyDictionary<string, string> After,
    IReadOnlyList<string> Corrections);

// One line of the journal (JSON Lines). The type tells which fields are set:
// - "run": the run starts (Run);
// - "batch": the edits about to be uploaded in a changeset, written before the
//   upload so nothing is lost if the run is interrupted (Batch, Area,
//   Changeset, Edits); the changeset is null in a dry run;
// - "uploaded": the server accepted the batch (Batch, Changeset, Versions);
// - "skip": a node left untouched (Node, User, Reason, Corrections);
// - "end": the run ends (Outcome).
public sealed record JournalRecord(string Type)
{
    public RunInfo? Run { get; init; }

    public int? Batch { get; init; }

    public string? Area { get; init; }

    public long? Changeset { get; init; }

    public IReadOnlyList<JournalEdit>? Edits { get; init; }

    // New version of every uploaded node.
    public IReadOnlyDictionary<long, int>? Versions { get; init; }

    public long? Node { get; init; }

    public string? User { get; init; }

    public string? Reason { get; init; }

    public IReadOnlyList<string>? Corrections { get; init; }

    public string? Outcome { get; init; }
}
