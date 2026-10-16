// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace ValidatorGen.Schema;

// What the wiki documents for one key: the values it lists and whether the value
// is free (a caption, a speed, a ref...) or restricted to that list.
public sealed record KeySpec(string Key, IReadOnlySet<string> Values, bool IsOpen)
{
    // True when the value carries a namespace prefix such as "FR:C", "CH-FDV:l"
    // or "ETCS:marker", which is what identifies a signal type key.
    public bool HasPrefixedValue => Values.Any(TagSchema.HasNamespacePrefix);
}
