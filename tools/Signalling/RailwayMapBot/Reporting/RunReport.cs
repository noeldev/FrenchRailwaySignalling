// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using RailwayMapBot.Core;
using RailwayMapBot.Planning;

namespace RailwayMapBot.Reporting;

// Writes the report of a run (Markdown), meant to be published: what was
// done, where, and a link to every changeset so the mappers can check their
// signals on the map instead of reading a log.
public static class RunReport
{
    public const string FileName = "report.md";

    private const int MaxMappers = 30;

    // Computed values (captions...) make many changes of one node each.
    private const int MaxTagChanges = 100;

    private static readonly IReadOnlyDictionary<SkipReason, string> SkipLabels = new Dictionary<SkipReason, string>
    {
        [SkipReason.NothingToFix] = "Nothing to fix any more (already fixed since the nodes were listed)",
        [SkipReason.Deleted] = "Deleted since the nodes were listed",
        [SkipReason.OutOfScope] = "No longer in scope",
        [SkipReason.OptedOut] = "Opted out (mapper or node)",
        [SkipReason.RecentlyEdited] = "Edited recently (quiet period)",
        [SkipReason.NeedsReview] = "Left to a human (guess, or manual complement needed)",
        [SkipReason.ChangedSince] = "Edited since the run (kept as it is)",
    };

    public static void Write(string path, RunInfo run, RunSummary summary, ServerSettings server, string documentation)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {run.Tool} - {run.Command} `{run.RunId}`");
        text.AppendLine();
        text.AppendLine(run.DryRun
            ? "**Dry run: nothing was written to the server.** The changesets below are what the run would upload."
            : "The changesets below were uploaded by this run.");
        text.AppendLine();
        text.AppendLine($"- Server: {run.Server} ({server.WebUrl})");
        text.AppendLine($"- Account: {(run.Account.Length > 0 ? run.Account : "-")}");
        text.AppendLine($"- Started: {run.Started.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
        text.AppendLine($"- Source: {run.Source}");
        text.AppendLine($"- Edit limit: {(run.EditLimit == int.MaxValue ? "none" : run.EditLimit)}");
        text.AppendLine($"- Rule table: sha256 `{run.RulesSha256}`");
        text.AppendLine($"- Documentation: {documentation}");
        text.AppendLine($"- Outcome: **{summary.Outcome}**");
        text.AppendLine();

        text.AppendLine("## Areas");
        text.AppendLine();
        text.AppendLine("| Area | Nodes listed | To fix or review | Edited |");
        text.AppendLine("|---|---:|---:|---:|");
        foreach (var area in summary.Areas)
        {
            text.AppendLine($"| {area.Name} | {area.Listed} | {area.Candidates} | {area.Edits} |");
        }

        text.AppendLine($"| **Total** | **{summary.Areas.Sum(area => area.Listed)}** | **{summary.Areas.Sum(area => area.Candidates)}** | **{summary.Areas.Sum(area => area.Edits)}** |");
        text.AppendLine();

        text.AppendLine("## Changesets");
        text.AppendLine();
        if (summary.Changesets.Count == 0)
        {
            text.AppendLine("None.");
        }
        else
        {
            text.AppendLine("| Batch | Area | Nodes | Changeset |");
            text.AppendLine("|---:|---|---:|---|");
            foreach (var changeset in summary.Changesets)
            {
                text.AppendLine($"| {changeset.Batch} | {changeset.Area} | {changeset.Edits} | {Link(changeset, server)} |");
            }
        }

        text.AppendLine();
        AppendCounts(text, "Tag changes", "Change (+ added, - removed, ~ changed)", summary.TagChanges, MaxTagChanges);
        AppendCounts(text, "Rules applied", "Rule (tagging found)", summary.Corrections, int.MaxValue);
        AppendSkips(text, summary);
        AppendCounts(text, "Rules left to a human", "Rule (tagging found)", summary.ReviewCorrections, int.MaxValue);
        AppendCounts(text, "Last mappers of the edited nodes", "Mapper", summary.Mappers, MaxMappers);

        File.WriteAllText(path, text.ToString());
    }

    private static string Link(ChangesetSummary changeset, ServerSettings server)
    {
        if (changeset.Id is not { } id)
        {
            return $"dry run (`changes-{changeset.Batch:000}.osc`)";
        }

        var link = $"[{id}]({server.WebUrl.TrimEnd('/')}/changeset/{id})";
        if (server.ReviewUrl.Length > 0)
        {
            link += $" ([review]({server.ReviewUrl.Replace("{id}", id.ToString(), StringComparison.Ordinal)}))";
        }

        return changeset.Done ? link : $"{link} - not uploaded";
    }

    private static void AppendSkips(StringBuilder text, RunSummary summary)
    {
        if (summary.Skips.Count == 0)
        {
            return;
        }

        text.AppendLine("## Nodes left untouched");
        text.AppendLine();
        text.AppendLine("| Reason | Nodes |");
        text.AppendLine("|---|---:|");
        foreach (var (reason, count) in summary.Skips.OrderByDescending(entry => entry.Value))
        {
            text.AppendLine($"| {SkipLabels[reason]} | {count} |");
        }

        text.AppendLine();
    }

    private static void AppendCounts(StringBuilder text, string title, string column, Dictionary<string, int> counts, int max)
    {
        if (counts.Count == 0)
        {
            return;
        }

        text.AppendLine($"## {title}");
        text.AppendLine();
        text.AppendLine($"| {column} | Nodes |");
        text.AppendLine("|---|---:|");
        foreach (var (label, count) in counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).Take(max))
        {
            text.AppendLine($"| `{label.Replace("|", "\\|", StringComparison.Ordinal)}` | {count} |");
        }

        if (counts.Count > max)
        {
            text.AppendLine($"| ({counts.Count - max} more) | {counts.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key, StringComparer.Ordinal).Skip(max).Sum(entry => entry.Value)} |");
        }

        text.AppendLine();
    }
}
