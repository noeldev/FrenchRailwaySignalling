// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;

namespace Signalling.OrmVector;

// Expands a regex-matched icon case into the values its "{}" placeholder takes,
// by brute force over the small numeric domain every such pattern in this
// project matches against (speeds, lengths and digit splits, 1 to 999). The
// placeholder takes the first capture group when the pattern has one (as the
// map does, e.g. one digit of a two-digit number), the whole value otherwise.
//
// Only closed patterns are expanded. A pattern with an unbounded quantifier
// ("+", "*", "{n,}") accepts any number, and the map draws only the values it
// has icons for, so enumerating it would report every number without an icon.
// A closed pattern that accepts no number (roman numerals, "a;b" pairs) also
// expands to nothing. In both cases the caller falls back to the example path
// the YAML case declares.
public static partial class TemplatedValueExpander
{
    private const int MinCandidate = 1;
    private const int MaxCandidate = 999;

    public static IReadOnlyList<string> Expand(string pattern)
    {
        if (UnboundedQuantifier().IsMatch(pattern))
        {
            return [];
        }

        var regex = new Regex(pattern);
        var values = new SortedSet<string>(StringComparer.Ordinal);

        for (var candidate = MinCandidate; candidate <= MaxCandidate; candidate++)
        {
            var match = regex.Match(candidate.ToString());
            if (match.Success)
            {
                values.Add(match.Groups.Count > 1 ? match.Groups[1].Value : match.Value);
            }
        }

        return [.. values];
    }

    [GeneratedRegex(@"(?<!\\)[*+]|\{\d+,\}")]
    private static partial Regex UnboundedQuantifier();
}
