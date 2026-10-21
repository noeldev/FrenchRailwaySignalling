// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;

namespace Signalling.Schema;

// Corrects the typing mistakes of the values of closed keys against the
// documented values, without enumerating them. Each token of a ";" separated
// value that is neither documented nor foreign is respelled; the correction is
// kept only when it leads to exactly one documented value of the key, compared
// case-insensitively (FR:cv -> FR:Cv, since the wiki documents FR:Cv only).
// The respellings:
// - case, alone or combined with the others;
// - missing country prefix (S -> FR:S), doubled prefix (FR:FR:S -> FR:S),
//   wrong or missing separator after it (FR!A, FR=A -> FR:A, FR(R) -> FR:(R));
// - leading punctuation (/sign -> sign, =FR:ID1 -> FR:ID1);
// - a colon used as list separator (FR:C:FR:S -> FR:C;FR:S), when every part
//   is documented.
// Empty entries (";;") are dropped, and the edge spaces of a documented token.
// Anything else is left as typed, for the validator to report.
public sealed partial class TokenNormalizer(TagSchema schema, string keyPrefix, string countryPrefix)
{
    private const char Separator = ';';

    private readonly string _prefix = countryPrefix + ":";

    public NormalizedTags Normalize(IReadOnlyDictionary<string, string> source)
    {
        var tags = new Dictionary<string, string>(source, StringComparer.Ordinal);
        List<TokenFix> fixes = [];

        foreach (var (key, value) in source.Where(tag => tag.Key.StartsWith(keyPrefix, StringComparison.Ordinal)))
        {
            if (!schema.TryGetKey(key, out var spec) || spec.IsOpen)
            {
                continue;
            }

            List<string> tokens = [];
            var fixCount = fixes.Count;
            foreach (var token in value.Split(Separator))
            {
                var trimmed = token.Trim();
                if (trimmed.Length == 0)
                {
                    fixes.Add(new TokenFix(key, token, string.Empty, "empty list entry"));
                }
                else if (spec.Values.Contains(trimmed) || schema.IsForeign(trimmed))
                {
                    if (trimmed != token)
                    {
                        fixes.Add(new TokenFix(key, token, trimmed, "edge spaces"));
                    }

                    tokens.Add(trimmed);
                }
                else if (TryRespell(spec, trimmed, out var respelled, out var kind))
                {
                    fixes.Add(new TokenFix(key, trimmed, respelled, kind));
                    tokens.Add(respelled);
                }
                else
                {
                    // Left as typed, for the validator to report.
                    tokens.Add(token);
                }
            }

            if (fixes.Count == fixCount)
            {
                continue;
            }

            var normalized = string.Join(Separator, tokens);
            if (normalized.Length == 0)
            {
                tags.Remove(key);
            }
            else
            {
                tags[key] = normalized;
            }
        }

        return new NormalizedTags(tags, fixes);
    }

    private bool TryRespell(KeySpec spec, string token, out string respelled, out string kind)
    {
        var candidates = Respellings(token)
            .Select(candidate => (Kind: candidate.Kind, Values: spec.Values.Where(value => string.Equals(value, candidate.Text, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(candidate => candidate.Values.Count > 0)
            .ToList();

        var values = candidates.SelectMany(candidate => candidate.Values).Distinct(StringComparer.Ordinal).ToList();
        if (values.Count == 1)
        {
            respelled = values[0];
            kind = candidates[0].Kind;
            return true;
        }

        var parts = ListSeparatorRegex().Split(token);
        if (parts.Length > 1 && parts.All(spec.Values.Contains))
        {
            respelled = string.Join(Separator, parts);
            kind = "colon used as list separator";
            return true;
        }

        respelled = string.Empty;
        kind = string.Empty;
        return false;
    }

    // The token as typed (for a case mistake), then with its prefix repaired,
    // then without its leading punctuation.
    private IEnumerable<(string Text, string Kind)> Respellings(string token)
    {
        yield return (token, "case");

        foreach (var repaired in RepairPrefix(token))
        {
            yield return repaired;
        }

        var stripped = LeadingPunctuationRegex().Replace(token, string.Empty);
        if (stripped.Length > 0 && stripped != token)
        {
            yield return (stripped, "leading punctuation");
            foreach (var (text, _) in RepairPrefix(stripped))
            {
                yield return (text, "leading punctuation");
            }
        }
    }

    private IEnumerable<(string Text, string Kind)> RepairPrefix(string token)
    {
        if (!token.Contains(':'))
        {
            yield return (_prefix + token, "missing prefix");
        }

        var doubled = DoubledPrefixRegex().Match(token);
        if (doubled.Success && string.Equals(doubled.Groups[1].Value, _prefix, StringComparison.OrdinalIgnoreCase))
        {
            yield return (_prefix + token[doubled.Length..], "doubled prefix");
        }

        // "FR" followed by another character than the colon: FR!A, FR=A
        // (wrong separator) or FR(R)+A (missing separator).
        var country = _prefix[..^1];
        if (token.Length > country.Length + 1
            && token.StartsWith(country, StringComparison.OrdinalIgnoreCase)
            && token[country.Length] != ':')
        {
            var rest = token[country.Length..];
            yield return (_prefix + rest[1..], "wrong separator after the prefix");
            yield return (_prefix + rest, "missing separator after the prefix");
        }
    }

    // "FR:FR:" and the like: the same prefix typed twice or more.
    [GeneratedRegex(@"^([A-Za-z]+:)(?:\1)+", RegexOptions.IgnoreCase)]
    private static partial Regex DoubledPrefixRegex();

    [GeneratedRegex(@"^[^\p{L}\p{N}(]+")]
    private static partial Regex LeadingPunctuationRegex();

    // A colon followed by another prefixed value: "FR:C:FR:S".
    [GeneratedRegex(@":(?=[A-Z]{2}:)")]
    private static partial Regex ListSeparatorRegex();
}

// The tags after the corrections, and the corrections made.
public sealed record NormalizedTags(IReadOnlyDictionary<string, string> Tags, IReadOnlyList<TokenFix> Fixes);

// One corrected token: the key, the token as found and as corrected (empty for
// a dropped entry), and the kind of mistake.
public sealed record TokenFix(string Key, string From, string To, string Kind);
