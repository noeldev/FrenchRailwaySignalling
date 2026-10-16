// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;

namespace ValidatorGen.MapCss;

// Low-level MapCSS spelling shared by the rule builders. The regular
// expressions are written in the subset that JOSM (Java) and Osmose-QA
// (Python) read the same way.
public static class MapCssSyntax
{
    private const string RegexSpecialCharacters = @"\.+*?^$()[]{}|/";

    // A double-quoted MapCSS string literal.
    public static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    // Escapes a literal for use inside a /.../ regular expression.
    public static string EscapeRegex(string literal)
    {
        var builder = new StringBuilder(literal.Length);
        foreach (var character in literal)
        {
            if (RegexSpecialCharacters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    // Alternation of escaped literals, without the enclosing group.
    public static string Alternation(IEnumerable<string> literals) => string.Join('|', literals.Select(EscapeRegex));

    // Case-insensitive spelling that does not rely on inline flags, for
    // example "FR" -> "[Ff][Rr]".
    public static string CaseInsensitive(string literal)
    {
        var builder = new StringBuilder();
        foreach (var character in literal)
        {
            var lower = char.ToLowerInvariant(character);
            var upper = char.ToUpperInvariant(character);
            if (lower == upper)
            {
                builder.Append(EscapeRegex(character.ToString()));
            }
            else
            {
                builder.Append('[').Append(upper).Append(lower).Append(']');
            }
        }

        return builder.ToString();
    }

    // The object of an assertMatch/assertNoMatch declaration.
    public static string AssertNode(IEnumerable<KeyValuePair<string, string>> tags) =>
        "node " + string.Join(' ', tags.Select(tag => $"{tag.Key}={tag.Value}"));
}
