// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;

namespace RailwayMapBot.Core;

// Turns the area codes typed by the operator into Overpass area statements,
// by the first kind of areaKinds whose pattern matches: for example "BRE" (a
// region) or "14" (a department). Codes are not case-sensitive. The
// configured areas give their name; for the others, Overpass gives it.
public sealed class AreaResolver(BotSettings settings)
{
    public const string CodePlaceholder = "{code}";

    // All the configured areas when no code is given.
    public IReadOnlyList<ResolvedArea> Resolve(IReadOnlyList<string> codes)
    {
        if (codes.Count == 0)
        {
            return [.. settings.Areas.Select(area => Resolve(area.Code))];
        }

        return [.. codes.Select(Resolve)];
    }

    public ResolvedArea Resolve(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (!TryFindKind(settings.AreaKinds, normalized, out var kind))
        {
            throw new ArgumentException($"Unknown area code: {code}. {Describe(settings.AreaKinds)}");
        }

        var name = settings.Areas.FirstOrDefault(area => area.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase))?.Name;
        return new ResolvedArea(normalized, kind.Selector.Replace(CodePlaceholder, normalized, StringComparison.Ordinal), name);
    }

    // The kinds of codes accepted, for the operator: "region (BRE, NOR), department (14, 75)".
    public static string Describe(IEnumerable<AreaKindSettings> kinds) =>
        "Accepted: " + string.Join(", ", kinds.Select(kind => $"{kind.Name} ({kind.Examples})")) + ".";

    public static bool TryFindKind(IEnumerable<AreaKindSettings> kinds, string code, out AreaKindSettings kind)
    {
        kind = kinds.FirstOrDefault(candidate => Regex.IsMatch(code, $"^(?:{candidate.Pattern})$", RegexOptions.IgnoreCase))!;
        return kind is not null;
    }
}
