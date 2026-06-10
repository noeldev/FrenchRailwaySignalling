// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace TagInfoGen;

// Project configuration bound from the "Project" section of appsettings.json by
// Microsoft.Extensions.Configuration, so that URLs, the contact address and the
// description language can be changed without recompiling. Property names are
// matched to the JSON keys case-insensitively by the configuration binder.
internal sealed class ProjectSettings
{
    // Base URL of the presets folder, used to turn relative asset paths
    // (icons) into absolute URLs.
    public string PresetUrl { get; set; } = string.Empty;

    // Required taginfo project fields.
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProjectUrl { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    // Optional taginfo project fields.
    public string? DocUrl { get; set; }
    public string? DataUrl { get; set; }

    // Project logo, relative to PresetUrl. Resolved to an absolute URL on output.
    public string IconPath { get; set; } = string.Empty;

    // When false, English labels are used for tag descriptions; when true, the
    // French (fr.*) variants are preferred.
    public bool UseFrenchDescriptions { get; set; }

    // Keys never emitted to taginfo.json (provenance metadata such as "source",
    // and generic OpenRailwayMap attribute keys already covered by their project).
    public List<string> ExcludedKeys { get; set; } = [];
}
