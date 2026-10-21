// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Schema;

namespace MapRouletteGen.Core;

// Configuration bound from appsettings.json (and the gitignored
// appsettings.local.json override). Property names are matched to the JSON keys
// case-insensitively by the configuration binder.
public sealed class GeneratorSettings
{
    // Wiki pages documenting the scheme, the same as ValidatorGen's.
    public WikiSettings Wiki { get; set; } = new();

    // Only keys under this prefix are checked, for example "railway:signal:".
    public string KeyPrefix { get; set; } = string.Empty;

    // Prefix of the documented values, for example "FR" for "FR:C".
    public string CountryPrefix { get; set; } = string.Empty;

    // Rule table shared with ValidatorGen, relative to the repository root.
    public string Rules { get; set; } = string.Empty;

    // Default challenge file, relative to the repository root. The challenge
    // texts (.md) are written next to it.
    public string Output { get; set; } = string.Empty;

    // Default test sample file, relative to the repository root.
    public string Samples { get; set; } = string.Empty;

    // Overpass QL query that exports the input file, one line per entry,
    // printed by --print-query.
    public List<string> OverpassQuery { get; set; } = [];

    public ChallengeSettings Challenge { get; set; } = new();
}

// Texts to paste into the MapRoulette challenge form (Markdown). In the
// instruction, {{legacy_rules}} is replaced by MapRoulette with the legacy
// tagging found on the task's signal.
public sealed class ChallengeSettings
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Instruction { get; set; } = string.Empty;
}
