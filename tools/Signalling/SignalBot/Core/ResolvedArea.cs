// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// An area to query: its code, its Overpass area statement and its name when
// known beforehand (the country); Overpass gives the name of the others.
public sealed record ResolvedArea(string Code, string Selector, string? Name);
