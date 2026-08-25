// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalAudit.Core;

// Bound from the "Sources" section of appsettings.json. Each source records a
// local path and a URL. Which one a bare flag uses is decided by the caller;
// the committed file holds only the public URLs, with local paths supplied by a
// gitignored appsettings.Local.json override or by launch arguments.
public sealed class SourcesSettings
{
    public SourceLocation OrmVectorYaml { get; set; } = new();

    public SourceLocation Wiki { get; set; } = new();
}

public sealed class SourceLocation
{
    public string? Local { get; set; }

    public string? Url { get; set; }
}
