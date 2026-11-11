// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Core;

// An area to query: its code, its Overpass area statement and its configured
// name, if any.
public sealed record ResolvedArea(string Code, string Selector, string? Name);
