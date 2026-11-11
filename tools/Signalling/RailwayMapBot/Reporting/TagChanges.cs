// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Reporting;

// The changes between the tags of a node before and after an edit, one line
// per key:
//   + key=value          tag added
//   - key=value          tag removed
//   ~ key: old -> new    value changed
public static class TagChanges
{
    public static IEnumerable<string> Describe(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            var had = before.TryGetValue(key, out var oldValue);
            var has = after.TryGetValue(key, out var newValue);
            if (had && has && oldValue != newValue)
            {
                yield return $"~ {key}: {oldValue} -> {newValue}";
            }
            else if (had && !has)
            {
                yield return $"- {key}={oldValue}";
            }
            else if (!had && has)
            {
                yield return $"+ {key}={newValue}";
            }
        }
    }
}
