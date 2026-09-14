// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.OrmVector;

// A single icon path referenced by an ORM-vector feature. PathTemplate is the
// raw value from the YAML; ValuePattern is the regex of the case it came from,
// when any (null for a default or an exact/any case). A case can use a regex
// purely to pick among several values while still pointing at one fixed icon,
// so IsTemplated goes by whether the path itself carries a "{}" placeholder,
// not by whether a regex was involved in matching it.
public sealed record IconReference(string PathTemplate, string? ValuePattern, string Origin)
{
    public bool IsTemplated => PathTemplate.Contains("{}", StringComparison.Ordinal);
}
