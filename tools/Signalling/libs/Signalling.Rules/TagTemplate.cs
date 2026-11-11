// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Text.RegularExpressions;

namespace Signalling.Rules;

// A tag value computed from other tags, for example the caption of a Km sign
// from its kilometric point:
//   "Km {railway:position:exact|1}"  with railway:position:exact=223.799
//   gives "Km 223,7".
// "{key}" is the value of the key, as tagged. "{key|n}" is its number,
// truncated (never rounded) to n decimals and written with a decimal comma,
// as on the French signs; the value must then be a plain decimal number
// (OSM spelling: optional minus sign, decimal point), or the template cannot
// be filled. A minus sign is dropped when every written digit is zero.
// Everything outside the braces is copied as is.
//
// sncf-sigmap fills its captions with the same notation and rules, so the
// tags it exports match the fixes of the validator, the challenge and the bot.
public sealed partial class TagTemplate
{
    // A plain decimal number, as railway:position:exact is tagged. Written in
    // the regular expression subset shared by JOSM and Osmose-QA.
    public const string NumberPattern = @"^-?[0-9]+(\.[0-9]+)?$";

    private const char DecimalComma = ',';

    private TagTemplate(string text, IReadOnlyList<TemplatePart> parts)
    {
        Text = text;
        Parts = parts;
    }

    public string Text { get; }

    public IReadOnlyList<TemplatePart> Parts { get; }

    // Keys read by the template, in order of appearance.
    public IEnumerable<string> Keys => Parts.Where(part => part.Key is not null).Select(part => part.Key!).Distinct(StringComparer.Ordinal);

    // Keys whose value must be a number (a placeholder with decimals).
    public IEnumerable<string> NumberKeys => Parts.Where(part => part.Decimals is not null).Select(part => part.Key!).Distinct(StringComparer.Ordinal);

    public static bool IsTemplate(string text) => text.Contains('{', StringComparison.Ordinal);

    public static TagTemplate Parse(string text)
    {
        List<TemplatePart> parts = [];
        var position = 0;
        foreach (Match match in PlaceholderRegex().Matches(text))
        {
            AddLiteral(parts, text[position..match.Index]);
            var decimals = match.Groups["decimals"].Success ? int.Parse(match.Groups["decimals"].Value) : (int?)null;
            parts.Add(new TemplatePart(null, match.Groups["key"].Value, decimals));
            position = match.Index + match.Length;
        }

        AddLiteral(parts, text[position..]);
        return new TagTemplate(text, parts);
    }

    // The value of the template for these tags, or null when a key is absent
    // or a number key does not hold a plain number.
    public string? Fill(IReadOnlyDictionary<string, string> tags)
    {
        var builder = new StringBuilder();
        foreach (var part in Parts)
        {
            if (part.Literal is not null)
            {
                builder.Append(part.Literal);
                continue;
            }

            if (!tags.TryGetValue(part.Key!, out var value))
            {
                return null;
            }

            if (part.Decimals is { } decimals)
            {
                var number = TruncateNumber(value, decimals);
                if (number is null)
                {
                    return null;
                }

                value = number;
            }

            builder.Append(value);
        }

        return builder.ToString();
    }

    // "223.799" truncated to 1 decimal gives "223,7"; "223" gives "223,0";
    // "-0.05" gives "0,0". Null when the value is not a plain number.
    public static string? TruncateNumber(string value, int decimals)
    {
        var match = NumberRegex().Match(value);
        if (!match.Success)
        {
            return null;
        }

        var integer = match.Groups["integer"].Value;
        var fraction = match.Groups["fraction"].Value.PadRight(decimals, '0')[..decimals];
        var sign = match.Groups["sign"].Value.Length > 0 && (integer + fraction).Any(digit => digit != '0') ? "-" : string.Empty;
        return decimals == 0 ? sign + integer : $"{sign}{integer}{DecimalComma}{fraction}";
    }

    private static void AddLiteral(List<TemplatePart> parts, string text)
    {
        if (text.Length > 0)
        {
            parts.Add(new TemplatePart(text, null, null));
        }
    }

    [GeneratedRegex(@"\{(?<key>[^{}|]+)(?:\|(?<decimals>[0-9]))?\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"^(?<sign>-?)(?<integer>[0-9]+)(?:\.(?<fraction>[0-9]+))?$")]
    private static partial Regex NumberRegex();
}

// A literal text, or a placeholder: the key and, for a number, its decimals.
public sealed record TemplatePart(string? Literal, string? Key, int? Decimals);
