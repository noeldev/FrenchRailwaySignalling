// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Presets;

namespace TagInfoGen;

// Turns the neutral preset tags into taginfo entries: resolves the raw icon
// paths into absolute URLs, then reconciles duplicates. The parser preserves
// emission order (items first, then unreferenced chunks), so merging keeps the
// first occurrence and the more specific item entries win.
//
// taginfo allows a single icon per entry, so conflicting icons are reconciled:
// state aspect keys (...:states) keep the first icon, while any other key drops
// the icon entirely, since the value alone does not pin a visual (the sign is
// told apart by another key or object type). The result is sorted by key then
// value for a stable output.
internal static class TagInfoBuilder
{
    private const string StatesKeySuffix = ":states";

    public static IReadOnlyList<TagInfoTag> Build(IReadOnlyList<PresetTag> tags, string baseUrl)
    {
        var mapped = tags.Select(tag => ToTagInfoTag(tag, baseUrl));
        return Deduplicate(mapped);
    }

    private static TagInfoTag ToTagInfoTag(PresetTag tag, string baseUrl) => new()
    {
        Key = tag.Key,
        Value = tag.Value,
        ObjectTypes = tag.ObjectTypes,
        Description = tag.Description,
        IconUrl = IconResolver.Resolve(baseUrl, tag.IconPath),
    };

    private static IReadOnlyList<TagInfoTag> Deduplicate(IEnumerable<TagInfoTag> tags) // CA1859
    {
        var merged = new Dictionary<string, TagInfoTag>(StringComparer.Ordinal);
        var iconDropped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            if (!merged.TryGetValue(tag.DedupKey, out var existing))
            {
                merged[tag.DedupKey] = tag;
                continue;
            }

            existing.Description ??= tag.Description;

            if (iconDropped.Contains(tag.DedupKey))
            {
                continue;
            }

            if (existing.IconUrl is null)
            {
                existing.IconUrl = tag.IconUrl;
            }
            else if (tag.IconUrl is not null
                && !string.Equals(existing.IconUrl, tag.IconUrl, StringComparison.Ordinal)
                && !KeepsFirstIconOnConflict(existing.Key))
            {
                existing.IconUrl = null;
                iconDropped.Add(tag.DedupKey);
            }
        }

        return [.. merged.Values
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .ThenBy(t => t.Value ?? string.Empty, StringComparer.Ordinal)];
    }

    // State aspect keys (railway:signal:*:states) keep the first declared icon
    // when several disagree; every other key drops the icon on conflict.
    private static bool KeepsFirstIconOnConflict(string key)
    {
        return key.EndsWith(StatesKeySuffix, StringComparison.Ordinal);
    }
}
