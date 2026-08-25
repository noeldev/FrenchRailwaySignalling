// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using System.Text.RegularExpressions;

namespace Signalling.Core.Model;

// A value constraint that a source expresses on a tag key. The same set of
// matcher shapes serves both roles: a producer (JOSM preset) lists the values it
// can emit through EnumerateValues, while a consumer (ORM-vector YAML) tests
// candidate values through Matches. A matcher that cannot enumerate its domain
// (open numeric ranges, regular expressions) yields no values and is compared at
// the key level only.
public abstract record Matcher
{
    // Concrete values this matcher can produce, or empty when the domain is
    // open-ended and only a key-level comparison is meaningful.
    public abstract IReadOnlyList<string> EnumerateValues();

    // True when the given value is accepted by this matcher.
    public abstract bool Matches(string value);
}

// Matches a single fixed value.
public sealed record ExactValue(string Value) : Matcher
{
    public override IReadOnlyList<string> EnumerateValues() => [Value];

    public override bool Matches(string value) =>
        string.Equals(value, Value, StringComparison.Ordinal);
}

// Matches any value from a closed set.
public sealed record AnyOf : Matcher
{
    public AnyOf(IEnumerable<string> values) => Values = [.. values];

    public IReadOnlyList<string> Values { get; }

    public override IReadOnlyList<string> EnumerateValues() => Values;

    public override bool Matches(string value) => Values.Contains(value, StringComparer.Ordinal);
}

// Matches values against a regular expression. The domain is open, so the
// matcher takes part in key-level comparison and value testing but enumerates
// nothing.
public sealed record RegexValue : Matcher
{
    private readonly Regex _regex;

    public RegexValue(string pattern)
    {
        Pattern = pattern;
        _regex = new Regex(pattern, RegexOptions.CultureInvariant);
    }

    public string Pattern { get; }

    public override IReadOnlyList<string> EnumerateValues() => [];

    public override bool Matches(string value) => _regex.IsMatch(value);
}

// Matches the mere presence of the key, whatever its value. Used by open-ended
// producer declarations (numeric combos) and by consumer rules that render a
// feature without discriminating on the value.
public sealed record KeyPresent : Matcher
{
    public override IReadOnlyList<string> EnumerateValues() => [];

    public override bool Matches(string value) => true;
}
