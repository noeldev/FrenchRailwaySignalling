// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;

namespace Signalling.OrmVector;

// Expands a regex-matched icon case into its concrete values by brute force
// over the small numeric domain every such pattern in this project matches
// against (railway speed limits, 1 to 999 km/h). This is not a general
// regex-to-language enumerator: it works because every observed pattern is an
// anchored digit alternation, and a full symbolic enumerator would be more
// machinery than that need justifies.
public static class TemplatedValueExpander
{
    private const int MinCandidate = 1;
    private const int MaxCandidate = 999;

    // The concrete values a regex pattern accepts within the numeric domain,
    // in ascending numeric order. Empty when the pattern matches none of them
    // (for example a non-numeric pattern), so the caller can fall back to
    // whatever example value the case declares.
    public static IReadOnlyList<string> Expand(string pattern)
    {
        var regex = new Regex(pattern);
        var matches = new List<string>();

        for (var candidate = MinCandidate; candidate <= MaxCandidate; candidate++)
        {
            var text = candidate.ToString();
            if (regex.IsMatch(text))
            {
                matches.Add(text);
            }
        }

        return matches;
    }
}
