// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Planning;

public static class TagComparer
{
    // True when both sets hold the same keys with the same values.
    public static bool AreEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(tag => right.TryGetValue(tag.Key, out var value) && value == tag.Value);
}
