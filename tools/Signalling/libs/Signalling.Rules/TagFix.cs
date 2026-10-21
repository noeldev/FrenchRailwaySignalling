// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Rules;

// The tags of an OSM object before and after the rule table was applied, the
// rules that changed them with their notes, and the legacy tagging left because
// its replacement would overwrite a tag (to be fixed by hand).
public sealed record TagFix(
    IReadOnlyDictionary<string, string> Original,
    IReadOnlyDictionary<string, string> Fixed,
    IReadOnlyList<string> AppliedRules,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> BlockedRules)
{
    public bool HasChanges => SetTags.Count > 0 || UnsetKeys.Count > 0;

    // Tags added or changed by the fix.
    public IReadOnlyDictionary<string, string> SetTags =>
        Fixed.Where(tag => !Original.TryGetValue(tag.Key, out var value) || value != tag.Value)
            .ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal);

    // Keys removed by the fix.
    public IReadOnlyList<string> UnsetKeys => [.. Original.Keys.Where(key => !Fixed.ContainsKey(key))];
}
