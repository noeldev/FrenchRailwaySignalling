// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

namespace Signalling.Core.Model;

// A single (key, constraint) declaration read from a source, tagged with a human
// readable origin so the comparator can point findings back to the preset item
// or the YAML feature that produced them.
public sealed record SignalTagRule(string Key, Matcher Matcher, string Origin);
