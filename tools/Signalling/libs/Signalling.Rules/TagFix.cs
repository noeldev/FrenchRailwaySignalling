// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Rules;

// The tags of an OSM object before and after the rule table was applied, and
// the rules that changed them (empty when the tags were already up to date).
public sealed record TagFix(
    IReadOnlyDictionary<string, string> Original,
    IReadOnlyDictionary<string, string> Fixed,
    IReadOnlyList<string> AppliedRules)
{
    public bool HasChanges => AppliedRules.Count > 0;

    // Tags added or changed by the fix.
    public IReadOnlyDictionary<string, string> SetTags =>
        Fixed.Where(tag => !Original.TryGetValue(tag.Key, out var value) || value != tag.Value)
            .ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal);

    // Keys removed by the fix.
    public IReadOnlyList<string> UnsetKeys => [.. Original.Keys.Where(key => !Fixed.ContainsKey(key))];
}
