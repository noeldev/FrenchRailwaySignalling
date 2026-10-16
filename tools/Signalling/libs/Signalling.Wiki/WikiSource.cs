// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;
using System.Text.RegularExpressions;
using Signalling.Core;
using Signalling.Core.Model;

namespace Signalling.Wiki;

// Reads an OpenStreetMap wiki page (the ORM Tagging in France specification, a
// Key page listing the states of a signal, the ETCS markers page) as a source of
// truth: the tags the documentation defines. The content is either the
// MediaWiki API response for action=parse&prop=wikitext (JSON), or raw wikitext
// when read from a local cache.
//
// Two line shapes are tag declarations; templates that appear in prose are
// ignored. The enclosing section heading is kept as the rule origin.
//
// Bullet lines, optionally bold: "* {{Tag|...}}" or "* '''{{Tag|...}}'''". Only
// the first Tag template of the line is the declaration:
//   {{Tag|key|value}}                         -> ExactValue(value)
//   {{Tag|key|}} or {{Tag|key||}}             -> wildcard: KeyPresent (the value
//                                                domain is documented elsewhere,
//                                                for example a :states subpage)
//   {{Tag|key||(...{{TagValue|key|A}}/...)}}  -> AnyOf(A, ...) from the TagValues
//
// Table cells that start with a template: "|{{TagValue|key|A}}" (a row of a
// states table) or "| {{Tag|key|A}}<br>{{Tag|...}}" (several tags in one cell).
// Every template of such a cell is a declaration of its own. Cells that start
// with prose are ignored, like any other prose.
public sealed partial class WikiSource(string country = "FR") : ISignalTagSource
{
    public string FormatName => "OSM wiki";

    public string Country { get; } = country;

    public IReadOnlyList<SignalTagRule> ReadRules(Stream content)
    {
        string text;
        using (var reader = new StreamReader(content, leaveOpen: true))
        {
            text = reader.ReadToEnd();
        }

        return ParseWikitext(ExtractWikitext(text));
    }

    // The API returns {"parse":{"wikitext":"..."}} (formatversion 2) or
    // {"parse":{"wikitext":{"*":"..."}}} (formatversion 1). A local cache may
    // hold raw wikitext instead; anything that is not that JSON shape is used
    // as-is.
    private static string ExtractWikitext(string content)
    {
        if (!content.TrimStart().StartsWith('{'))
        {
            return content;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("parse", out var parse)
                && parse.TryGetProperty("wikitext", out var wikitext))
            {
                if (wikitext.ValueKind == JsonValueKind.Object && wikitext.TryGetProperty("*", out var star))
                {
                    return star.GetString() ?? string.Empty;
                }

                return wikitext.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not the expected JSON; treat the content as raw wikitext.
        }

        return content;
    }

    private static IReadOnlyList<SignalTagRule> ParseWikitext(string wikitext)
    {
        var rules = new List<SignalTagRule>();
        var heading = "(top)";

        foreach (var rawLine in wikitext.Split('\n'))
        {
            var line = rawLine.Trim();

            var headingMatch = HeadingRegex().Match(line);
            if (headingMatch.Success)
            {
                heading = headingMatch.Groups[1].Value.Trim();
                continue;
            }

            if (BulletRegex().IsMatch(line))
            {
                var rule = ParseTagLine(line, heading);
                if (rule is not null)
                {
                    rules.Add(rule);
                }
            }
            else if (TableCellRegex().IsMatch(line))
            {
                rules.AddRange(ParseTableCell(line, heading));
            }
        }

        return rules;
    }

    private static SignalTagRule? ParseTagLine(string line, string heading)
    {
        var keyMatch = TagRegex().Match(line);
        if (!keyMatch.Success)
        {
            return null;
        }

        var key = keyMatch.Groups[1].Value.Trim();

        // Alternatives are expressed as TagValue templates inside the Tag; when
        // present they carry the actual accepted values.
        var values = TagValueRegex().Matches(line).Select(match => match.Groups[2].Value.Trim()).ToList();
        if (values.Count > 0)
        {
            return new SignalTagRule(key, new AnyOf(values), heading);
        }

        return new SignalTagRule(key, ToMatcher(keyMatch.Groups[2].Value), heading);
    }

    // A cell holds either Tag templates (each one a full declaration) or TagValue
    // templates (each one a value of the key it names), never prose.
    private static IEnumerable<SignalTagRule> ParseTableCell(string line, string heading)
    {
        var tags = TagRegex().Matches(line);
        if (tags.Count > 0)
        {
            return tags.Select(match => new SignalTagRule(match.Groups[1].Value.Trim(), ToMatcher(match.Groups[2].Value), heading));
        }

        return TagValueRegex().Matches(line)
            .Select(match => new SignalTagRule(match.Groups[1].Value.Trim(), new ExactValue(match.Groups[2].Value.Trim()), heading));
    }

    private static Matcher ToMatcher(string plainValue)
    {
        var value = plainValue.Trim();
        return string.IsNullOrEmpty(value) ? new KeyPresent() : new ExactValue(value);
    }

    // A wiki heading: == ... == up to ====== ... ======. Group 1 is the title.
    [GeneratedRegex(@"^={2,6}\s*(.+?)\s*={2,6}$")]
    private static partial Regex HeadingRegex();

    // A bullet whose first item is a Tag template, possibly wrapped in bold.
    [GeneratedRegex(@"^\*\s*(?:'''\s*)?\{\{Tag\|")]
    private static partial Regex BulletRegex();

    // A table cell that starts with a Tag or TagValue template. Row separators
    // ("|-"), captions ("|+") and table ends ("|}") never match.
    [GeneratedRegex(@"^\|\s*\{\{Tag(?:Value)?\|")]
    private static partial Regex TableCellRegex();

    // The Tag template: group 1 is the key, group 2 is the plain value (empty for
    // a wildcard or when alternatives follow).
    [GeneratedRegex(@"\{\{Tag\|([^|}]+)\|([^|}]*)")]
    private static partial Regex TagRegex();

    // A TagValue template: group 1 is the key, group 2 is the accepted value.
    [GeneratedRegex(@"\{\{TagValue\|([^|}]+)\|([^}|]+)\}\}")]
    private static partial Regex TagValueRegex();
}
