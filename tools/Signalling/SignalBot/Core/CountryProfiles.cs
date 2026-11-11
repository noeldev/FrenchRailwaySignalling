// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// The country profiles shipped with the program (countries/<code>.json): one
// per country, each with its scheme, rule table, areas and comments, so the
// same bot serves several countries without mixing their data.
public static class CountryProfiles
{
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "countries");

    // Codes of the available profiles, sorted.
    public static IReadOnlyList<string> Codes() =>
        Directory.Exists(Folder)
            ? [.. Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)]
            : [];

    public static string PathOf(string code) => Path.Combine(Folder, $"{code}.json");

    // The given profile, or the only one when none is given.
    public static string Resolve(string? code)
    {
        var codes = Codes();
        if (code is null)
        {
            return codes.Count == 1
                ? codes[0]
                : throw new InvalidOperationException($"Several country profiles ({string.Join(", ", codes)}): choose one with --country.");
        }

        return codes.FirstOrDefault(candidate => candidate.Equals(code, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown country: {code}. Profiles: {string.Join(", ", codes)}.");
    }
}
