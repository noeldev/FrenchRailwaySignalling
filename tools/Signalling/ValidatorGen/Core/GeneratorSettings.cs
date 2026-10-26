// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Schema;

namespace ValidatorGen.Core;

// Configuration bound from appsettings.json (and the gitignored
// appsettings.local.json override). Property names are matched to the JSON keys
// case-insensitively by the configuration binder.
public sealed class GeneratorSettings
{
    public WikiSettings Wiki { get; set; } = new();

    public ValidatorSettings Validator { get; set; } = new();
}

public sealed class ValidatorSettings
{
    // MapCSS meta block, shown by JOSM in the rules list.
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public int MinJosmVersion { get; set; }

    // Only keys under this prefix are checked, for example "railway:signal:".
    public string KeyPrefix { get; set; } = string.Empty;

    // A node is in scope when one of its checked keys has a value with this
    // country prefix, for example "FR" for "FR:C".
    public string CountryPrefix { get; set; } = string.Empty;

    // Used in messages, for example "French railway signalling scheme".
    public string SchemeName { get; set; } = string.Empty;

    // Osmose-QA tags applied to every rule.
    public List<string> OsmoseTags { get; set; } = [];

    // Rule table (replacements and conflicts), relative to the repository root.
    public string Rules { get; set; } = string.Empty;

    // Default output file, relative to the repository root.
    public string Output { get; set; } = string.Empty;
}
