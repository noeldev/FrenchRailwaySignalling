// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using Signalling.Core.Model;

namespace SignalAudit.Validators;

// Preset values and keys that implement the ETCS marker harmonization scheme
// (see the OpenRailwayMap/ETCS_Markers wiki page) but that neither the
// Tagging_in_France wiki page nor the ORM-vector map document yet. The ETCS
// page is a separate page from Tagging_in_France, and it states its own
// markers are not rendered on the map yet, so neither cross-source check can
// confirm these values without also reading that second page.
//
// This is an explicit allow-list, not a namespace-wide exemption: anything not
// listed here, including a typo in a new ETCS value, is still compared
// normally. Update this list when either downstream adopts ETCS markers, or
// when the preset's ETCS coverage changes.
internal static class EtcsPendingCoverage
{
    private static readonly HashSet<(string Key, string Value)> PendingValues = new()
    {
        ("railway:signal:electricity", "ETCS:end_of_catenary"),
        ("railway:signal:electricity", "ETCS:end_of_catenary_advance"),
        ("railway:signal:electricity", "ETCS:pantograph_down"),
        ("railway:signal:electricity", "ETCS:pantograph_down_advance"),
        ("railway:signal:electricity", "ETCS:pantograph_up"),
        ("railway:signal:electricity", "ETCS:power_off"),
        ("railway:signal:electricity", "ETCS:power_off_advance"),
        ("railway:signal:electricity", "ETCS:power_on"),
        ("railway:signal:electricity:type", "end_of_catenary_advance"),
        ("railway:signal:train_protection", "ETCS:marker"),
        ("railway:signal:train_protection:main", "ETCS:location_marker"),
        ("railway:signal:train_protection:main", "ETCS:stop_marker"),
        ("railway:signal:train_protection:system_change", "ETCS:level_transition"),
    };

    private static readonly HashSet<string> PendingKeys = new(StringComparer.Ordinal)
    {
        "railway:signal:train_protection:main:caption",
        "railway:signal:train_protection:main:function",
    };

    public static IReadOnlyList<SignalTagRule> ExcludePending(IReadOnlyList<SignalTagRule> producerRules)
    {
        return [.. producerRules.Where(rule =>
            !PendingKeys.Contains(rule.Key)
            && !rule.Matcher.EnumerateValues().Any(value => PendingValues.Contains((rule.Key, value))))];
    }
}
