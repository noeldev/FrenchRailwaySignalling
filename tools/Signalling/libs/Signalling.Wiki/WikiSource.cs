// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;
using System.Text.RegularExpressions;
using Signalling.Core;
using Signalling.Core.Model;

namespace Signalling.Wiki;

// Reads an OpenStreetMap wiki page (the ORM Tagging in France specification) as
// a source of truth: the tags the documentation defines. The content is either
// the MediaWiki API response for action=parse&prop=wikitext (JSON), or raw
// wikitext when read from a local cache.
//
// Only bullet lines of the form "* {{Tag|...}}" are tag declarations; TagValue
// templates that appear in prose are ignored. The enclosing section heading is
// kept as the rule origin.
//
// Tag line shapes:
//   {{Tag|key|value}}                         -> ExactValue(value)
//   {{Tag|key|}} or {{Tag|key||}}             -> wildcard: KeyPresent (the value
//                                                domain is documented elsewhere,
//                                                for example a :states subpage)
//   {{Tag|key||(...{{TagValue|key|A}}/...)}}  -> AnyOf(A, ...) from the TagValues
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

            if (!line.StartsWith("* {{Tag|", StringComparison.Ordinal)
                && !line.StartsWith("*{{Tag|", StringComparison.Ordinal))
            {
                continue;
            }

            var rule = ParseTagLine(line, heading);
            if (rule is not null)
            {
                rules.Add(rule);
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

        var key = keyMatch.Groups[1].Value;

        // Alternatives are expressed as TagValue templates inside the Tag; when
        // present they carry the actual accepted values.
        var values = TagValueRegex().Matches(line).Select(match => match.Groups[1].Value).ToList();
        if (values.Count > 0)
        {
            return new SignalTagRule(key, new AnyOf(values), heading);
        }

        var plainValue = keyMatch.Groups[2].Value;
        Matcher matcher = string.IsNullOrEmpty(plainValue) ? new KeyPresent() : new ExactValue(plainValue);
        return new SignalTagRule(key, matcher, heading);
    }

    // A wiki heading: == ... == up to ====== ... ======. Group 1 is the title.
    [GeneratedRegex(@"^={2,6}\s*(.+?)\s*={2,6}$")]
    private static partial Regex HeadingRegex();

    // The Tag template: group 1 is the key, group 2 is the plain value (empty for
    // a wildcard or when alternatives follow).
    [GeneratedRegex(@"\{\{Tag\|([^|}]+)\|([^|}]*)")]
    private static partial Regex TagRegex();

    // A TagValue template: group 1 is the accepted value.
    [GeneratedRegex(@"\{\{TagValue\|[^|}]+\|([^}|]+)\}\}")]
    private static partial Regex TagValueRegex();
}
