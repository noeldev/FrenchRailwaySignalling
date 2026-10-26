// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Core.Model;

namespace SignalAudit.Validators;

// Deliberate differences between what the preset writes and what the map
// renders, so the preset/map comparison only reports real gaps:
// - render-only keys and values are read by the map but never written by the
//   preset: "main:etcs" draws a TVM and an ETCS stop marker side by side for
//   legacy data mapped on one node, "states=off" draws an unlit signal (the
//   states of a TIDD, a TLC or a switchable TIV are declared by the preset);
// - unrendered values are written by the preset but have no effect on the
//   icon: the map only discriminates "main:function=intermediate" (2TMV), the
//   other functions are drawn like any main signal, and a switchable TIV is
//   drawn closed (with its speed) unless its states say open or off.
// This is an explicit list, not a pattern: anything else is compared normally.
internal static class MapRenderExceptions
{
    private const string AnyValue = "*";

    private static readonly HashSet<(string Key, string Value)> RenderOnly =
    [
        ("railway:signal:train_protection:main:etcs", AnyValue),
        ("railway:signal:main:states", "off"),
        ("railway:signal:distant:states", "off"),
    ];

    private static readonly HashSet<(string Key, string Value)> Unrendered =
    [
        ("railway:signal:main:function", "block"),
        ("railway:signal:main:function", "entry"),
        ("railway:signal:main:function", "exit"),
        ("railway:signal:speed_limit:switchable:states", "closed"),
        ("railway:signal:speed_limit_distant:switchable:states", "closed"),
    ];

    public static IReadOnlyList<SignalTagRule> ExcludeRenderOnly(IReadOnlyList<SignalTagRule> consumerRules) =>
        [.. consumerRules.Where(rule => !IsListed(rule, RenderOnly))];

    public static IReadOnlyList<SignalTagRule> ExcludeUnrendered(IReadOnlyList<SignalTagRule> producerRules) =>
        [.. producerRules.Where(rule => !IsListed(rule, Unrendered))];

    // A rule is dropped when its whole key is listed, or when every value it
    // matches is listed for its key.
    private static bool IsListed(SignalTagRule rule, HashSet<(string Key, string Value)> list) =>
        list.Contains((rule.Key, AnyValue))
        || rule.Matcher.EnumerateValues() is { Count: > 0 } values && values.All(value => list.Contains((rule.Key, value)));
}
