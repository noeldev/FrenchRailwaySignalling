// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace MapRouletteGen.Core;

// Configuration bound from appsettings.json (and the gitignored
// appsettings.local.json override). Property names are matched to the JSON keys
// case-insensitively by the configuration binder.
public sealed class GeneratorSettings
{
    // Rule table shared with ValidatorGen, relative to the repository root.
    public string Rules { get; set; } = string.Empty;

    // Default challenge file, relative to the repository root. The challenge
    // texts are written next to it, with the .md extension.
    public string Output { get; set; } = string.Empty;

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
