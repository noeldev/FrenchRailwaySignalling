// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using RailwayMapBot.Core;
using Signalling.Rules;

namespace RailwayMapBot.Reporting;

// Writes the exact list of the corrections the bot makes, as wikitext tables
// for the page documenting the edit: an automated edit is approved for a
// stated list of changes, not for "fixing tags". The list comes from the rule
// table and the policy, so it cannot drift from what the bot does.
public static class CorrectionListWriter
{
    public const string FileName = "corrections.wiki";

    public static void Write(string path, RuleTable table, PolicySettings policy, string rulesHash)
    {
        var automated = table.TagReplacements.Where(entry => !IsLeftToMappers(entry, policy)).ToList();
        var manual = table.TagReplacements.Where(entry => IsLeftToMappers(entry, policy)).ToList();

        var text = new StringBuilder();
        text.AppendLine($"Rule table: <code>sha256 {rulesHash}</code>.");
        text.AppendLine();

        AppendTable(text, $"Renamed values ({table.ValueReplacements.Count})", ["Key", "Old value", "New value"],
            table.ValueReplacements.Select(entry => new[] { Code(entry.Key), Code(entry.From), entry.To.Length > 0 ? Code(entry.To) : "(removed)" }));

        AppendTable(text, $"Replaced tagging ({automated.Count})", ["Tagging found", "Change"],
            automated.Select(entry => new[] { Tagging(entry), Change(entry) }));

        AppendTable(text, $"Redundant tags removed ({table.Redundancies.Count})", ["Tagging found", "Change", "Reason"],
            table.Redundancies.Select(entry => new[] { Tagging(entry), Change(entry), entry.Note ?? string.Empty }));

        AppendTable(text, $"Not automated, left to mappers ({manual.Count})", ["Tagging found", "Reason"],
            manual.Select(entry => new[] { Tagging(entry), Reason(entry) }));

        File.WriteAllText(path, text.ToString());
    }

    // A replacement that only reports, guesses, or needs a manual complement.
    private static bool IsLeftToMappers(TagReplacement entry, PolicySettings policy) =>
        entry.IsReportOnly
        || (policy.SkipSuggestions && entry.Suggest)
        || (policy.SkipIncomplete && !entry.Suggest && !string.IsNullOrWhiteSpace(entry.Note));

    private static string Reason(TagReplacement entry) =>
        entry.IsReportOnly ? $"reported only: {entry.Note}"
        : entry.Suggest ? "the replacement is a guess"
        : $"needs a manual complement: {entry.Note}";

    private static string Tagging(TagReplacement entry) =>
        string.Join(" + ", entry.Match.Select(tag => tag.Value is null ? $"no {Code(tag.Key)}" : Code($"{tag.Key}={tag.Value}")));

    private static string Change(TagReplacement entry)
    {
        var steps = entry.Rename.Select(tag => $"rename {Code(tag.Key)} to {Code(tag.Value)}")
            .Concat(entry.Remove.Select(key => $"remove {Code(key)}"))
            .Concat(entry.Set.Select(tag => $"set {Code($"{tag.Key}={tag.Value}")}"))
            .Concat(entry.Default.Select(tag => $"set {Code($"{tag.Key}={tag.Value}")} if absent"));

        return string.Join("; ", steps);
    }

    // A pipe would end the table cell.
    private static string Code(string value) => $"<code>{value.Replace("|", "&#124;", StringComparison.Ordinal)}</code>";

    private static void AppendTable(StringBuilder text, string title, string[] columns, IEnumerable<string[]> rows)
    {
        text.AppendLine("{| class=\"wikitable mw-collapsible mw-collapsed\"");
        text.AppendLine($"|+ {title}");
        text.AppendLine("! " + string.Join(" !! ", columns));
        foreach (var row in rows)
        {
            text.AppendLine("|-");
            text.AppendLine("| " + string.Join(" || ", row));
        }

        text.AppendLine("|}");
        text.AppendLine();
    }
}
