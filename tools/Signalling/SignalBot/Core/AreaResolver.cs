// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;

namespace SignalBot.Core;

// Turns the area codes of a filter into Overpass area statements, by the
// first kind of areaKinds whose pattern matches: for example "BRE" (a region)
// or "14" (a department). Codes are not case-sensitive. Without a code, the
// area is the whole country.
public sealed class AreaResolver(BotSettings settings)
{
    public const string CodePlaceholder = "{code}";

    public IReadOnlyList<ResolvedArea> Resolve(IReadOnlyList<string> codes) =>
        codes.Count == 0
            ? [new ResolvedArea(settings.Country.Code, settings.Country.Selector, settings.Country.Name)]
            : [.. codes.Select(Resolve)];

    public ResolvedArea Resolve(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (!TryFindKind(settings.AreaKinds, normalized, out var kind))
        {
            throw new ArgumentException($"Unknown area code: {code}. Accepted:{Environment.NewLine}{Describe(settings.AreaKinds)}");
        }

        return new ResolvedArea(normalized, kind.Selector.Replace(CodePlaceholder, normalized, StringComparison.Ordinal), null);
    }

    // The kinds of codes accepted, for the operator, one per line:
    // "region: ISO 3166-2 code without the FR- prefix (BRE, NOR)".
    public static string Describe(IEnumerable<AreaKindSettings> kinds) =>
        string.Join(Environment.NewLine, kinds.Select(kind => $"  {kind.Name}: {kind.Format} ({kind.Examples})"));

    public static bool TryFindKind(IEnumerable<AreaKindSettings> kinds, string code, out AreaKindSettings kind)
    {
        kind = kinds.FirstOrDefault(candidate => Regex.IsMatch(code, $"^(?:{candidate.Pattern})$", RegexOptions.IgnoreCase))!;
        return kind is not null;
    }
}
