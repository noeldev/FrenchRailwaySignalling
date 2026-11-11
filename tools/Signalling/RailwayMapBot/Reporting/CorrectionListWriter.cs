// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using RailwayMapBot.Core;
using Signalling.Rules;

namespace RailwayMapBot.Reporting;

// Writes the exact list of the corrections the bot makes: an automated edit
// is approved for a stated list of changes, not for "fixing tags". The list
// comes from the rule table and the policy, so it cannot drift from what the
// bot does. Two files hold the same tables:
// - corrections.md, to read it locally;
// - corrections.wiki, to paste into the wiki page documenting the edit.
public static class CorrectionListWriter
{
    public const string MarkdownFileName = "corrections.md";
    public const string WikiFileName = "corrections.wiki";

    // Writes both files into the folder and returns their paths.
    public static IReadOnlyList<string> Write(string folder, RuleTable table, PolicySettings policy, string rulesHash)
    {
        var markdown = Path.Combine(folder, MarkdownFileName);
        var wiki = Path.Combine(folder, WikiFileName);
        File.WriteAllText(markdown, Render(new MarkdownFormat(), table, policy, rulesHash));
        File.WriteAllText(wiki, Render(new WikiFormat(), table, policy, rulesHash));
        return [markdown, wiki];
    }

    private static string Render(ITableFormat format, RuleTable table, PolicySettings policy, string rulesHash)
    {
        var automated = table.TagReplacements.Where(entry => !IsLeftToMappers(entry, policy)).ToList();
        var manual = table.TagReplacements.Where(entry => IsLeftToMappers(entry, policy)).ToList();
        var code = format.Code;

        var text = new StringBuilder();
        text.AppendLine($"Rule table: {code($"sha256 {rulesHash}")}.");
        text.AppendLine();

        format.AppendTable(text, $"Renamed values ({table.ValueReplacements.Count})", ["Key", "Old value", "New value"],
            table.ValueReplacements.Select(entry => new[] { code(entry.Key), code(entry.From), entry.To.Length > 0 ? code(entry.To) : "(removed)" }));

        format.AppendTable(text, $"Replaced tagging ({automated.Count})", ["Tagging found", "Change"],
            automated.Select(entry => new[] { Tagging(entry, code), Change(entry, code) }));

        format.AppendTable(text, $"Redundant tags removed ({table.Redundancies.Count})", ["Tagging found", "Change", "Reason"],
            table.Redundancies.Select(entry => new[] { Tagging(entry, code), Change(entry, code), entry.Note ?? string.Empty }));

        format.AppendTable(text, $"Computed tags added ({table.Completions.Count})", ["Tagging found", "Change"],
            table.Completions.Select(entry => new[] { Tagging(entry, code), Change(entry, code) }));

        format.AppendTable(text, $"Not automated, left to mappers ({manual.Count})", ["Tagging found", "Reason"],
            manual.Select(entry => new[] { Tagging(entry, code), Reason(entry) }));

        return text.ToString();
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

    private static string Tagging(TagReplacement entry, Func<string, string> code) =>
        string.Join(" + ", entry.Match.Select(tag => tag.Value is null ? $"no {code(tag.Key)}" : code($"{tag.Key}={tag.Value}")));

    private static string Change(TagReplacement entry, Func<string, string> code)
    {
        var steps = entry.Rename.Select(tag => $"rename {code(tag.Key)} to {code(tag.Value)}")
            .Concat(entry.Remove.Select(key => $"remove {code(key)}"))
            .Concat(entry.Set.Select(tag => $"set {code($"{tag.Key}={tag.Value}")}"))
            .Concat(entry.Default.Select(tag => $"set {code($"{tag.Key}={tag.Value}")} if absent"));

        return string.Join("; ", steps);
    }

    private interface ITableFormat
    {
        string Code(string value);

        void AppendTable(StringBuilder text, string title, string[] columns, IEnumerable<string[]> rows);
    }

    // Collapsed wikitext tables. A pipe would end the table cell.
    private sealed class WikiFormat : ITableFormat
    {
        public string Code(string value) => $"<code>{value.Replace("|", "&#124;", StringComparison.Ordinal)}</code>";

        public void AppendTable(StringBuilder text, string title, string[] columns, IEnumerable<string[]> rows)
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

    // Markdown tables, one section each. A pipe would end the table cell.
    private sealed class MarkdownFormat : ITableFormat
    {
        public string Code(string value) => $"`{value.Replace("|", "\\|", StringComparison.Ordinal)}`";

        public void AppendTable(StringBuilder text, string title, string[] columns, IEnumerable<string[]> rows)
        {
            text.AppendLine($"## {title}");
            text.AppendLine();
            text.AppendLine("| " + string.Join(" | ", columns) + " |");
            text.AppendLine("|" + string.Concat(columns.Select(_ => "---|")));
            foreach (var row in rows)
            {
                text.AppendLine("| " + string.Join(" | ", row) + " |");
            }

            text.AppendLine();
        }
    }
}
