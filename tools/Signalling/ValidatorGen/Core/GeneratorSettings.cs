// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace ValidatorGen.Core;

// Configuration bound from appsettings.json (and the gitignored
// appsettings.local.json override). Property names are matched to the JSON keys
// case-insensitively by the configuration binder.
public sealed class GeneratorSettings
{
    public WikiSettings Wiki { get; set; } = new();

    public ValidatorSettings Validator { get; set; } = new();
}

public sealed class WikiSettings
{
    // MediaWiki API endpoint used to download the pages with their revision
    // timestamp in a single request.
    public string ApiUrl { get; set; } = string.Empty;

    public List<WikiPageSettings> Pages { get; set; } = [];
}

public sealed class WikiPageSettings
{
    public string Title { get; set; } = string.Empty;

    public WikiPageRole Role { get; set; } = WikiPageRole.Scheme;

    // Local copy (relative to the repository root), required with --local.
    public string? Local { get; set; }
}

// Scheme pages declare keys and values; a value list given there with a bare
// {{Tag|key|}} wildcard stays open. Domain pages (the Key pages listing the
// states of a signal) close the value list of every key they document.
public enum WikiPageRole
{
    Scheme,
    Domain,
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
