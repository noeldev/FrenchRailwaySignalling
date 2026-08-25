// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Core.Sources;

namespace SignalAudit.Core;

// Describes a resolved source (name and, when known, modification time) so the
// report states exactly which version of each input was compared.
public static class SourceProvenance
{
    public static string Describe(ResolvedSource source) =>
        source.LastModified is { } modified
            ? $"{source.DisplayName} (modified {modified.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC)"
            : source.DisplayName;
}
