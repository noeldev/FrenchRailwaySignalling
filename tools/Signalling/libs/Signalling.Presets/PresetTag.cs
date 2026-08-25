// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Presets;

// A single tag declaration extracted from a JOSM preset, in a neutral shape that
// carries no taginfo serialization concern. IconPath is the raw relative path as
// written in the preset, left unresolved so that each consumer resolves it (or
// ignores it) as it sees fit. IsOpenDomain marks the open-ended numeric combos
// (speeds) that are declared as a bare key: they enumerate no value and are only
// meaningful for a key-level comparison.
public sealed record PresetTag
{
    public required string Key { get; init; }

    public string? Value { get; init; }

    public required IReadOnlyList<string> ObjectTypes { get; init; }

    public string? Description { get; init; }

    // Relative icon path as declared in the preset, unresolved. Null when none.
    public string? IconPath { get; init; }

    // True for open-ended numeric combos emitted as a key-only declaration.
    public bool IsOpenDomain { get; init; }
}
