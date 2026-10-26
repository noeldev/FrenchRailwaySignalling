// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Schema;

// The wiki pages that document the scheme, bound from the "wiki" section of a
// tool's appsettings.json.
public sealed class WikiSettings
{
    // MediaWiki API endpoint used to download the pages with their revision
    // timestamp in a single request.
    public string ApiUrl { get; set; } = string.Empty;

    public List<WikiPageSettings> Pages { get; set; } = [];
}

public sealed class WikiPageSettings
{
    public string Title { get; set; } = string.Empty;

    public WikiPageRole Role { get; set; } = WikiPageRole.Scheme;

    // Local copy (relative to the repository root), required with --local.
    public string? Local { get; set; }
}

// Scheme pages declare keys and values; a value list given there with a bare
// {{Tag|key|}} wildcard stays open. Domain pages (the Key pages listing the
// states of a signal) close the value list of every key they document.
public enum WikiPageRole
{
    Scheme,
    Domain,
}
