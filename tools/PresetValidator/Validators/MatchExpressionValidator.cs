// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;
using System.Xml.Linq;
using PresetValidator.Core;

namespace PresetValidator.Validators;

// Enforces a local convention around match_expression. A key quoted inside an
// item match_expression is matched through that expression, so its own <key>
// declaration in the same item must opt out of tag matching with match="none".
// A quoted key whose <key> still participates (no match attribute, which
// defaults to a matching mode, or a match other than "none") would match the
// tag twice, so it is reported as a warning. Only keys quoted in the expression
// and declared inline are checked; unquoted keys and keys pulled in through a
// <reference> are left alone.
public sealed partial class MatchExpressionValidator : IValidator
{
    public string Name => "Match expressions";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();

        foreach (var item in context.Document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            var expression = item.Attribute("match_expression")?.Value;
            if (string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            var quotedKeys = ExtractQuotedKeys(expression);
            if (quotedKeys.Count == 0)
            {
                continue;
            }

            foreach (var key in item.Descendants().Where(e => e.Name.LocalName == "key"))
            {
                var keyName = key.Attribute("key")?.Value;
                if (keyName is null || !quotedKeys.Contains(keyName))
                {
                    continue;
                }

                var match = key.Attribute("match")?.Value;
                if (!string.Equals(match, "none", StringComparison.Ordinal))
                {
                    issues.Add(key.ToIssue(
                        ValidationSeverity.Warning,
                        $"Key '{keyName}' is used in match_expression; its <key> should set match=\"none\"."));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    // Collects double-quoted tokens sitting in key position, that is, a quoted
    // string immediately followed by a comparison operator (for example the
    // "railway:signal:distant:type" in -"railway:signal:distant:type"=*). A
    // quoted value, which follows an operator instead, is not captured.
    private static HashSet<string> ExtractQuotedKeys(string expression)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in QuotedKeyRegex().Matches(expression))
        {
            keys.Add(match.Groups[1].Value);
        }

        return keys;
    }

    [GeneratedRegex("\"([^\"]+)\"\\s*[=~<>]")]
    private static partial Regex QuotedKeyRegex();
}
