// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou


namespace Signalling.Schema;

// One wiki page as read for the generation: its wikitext, the revision time
// (or file time for a local copy) and where it was read from.
public sealed record WikiPage(string Title, WikiPageRole Role, string Wikitext, DateTimeOffset Timestamp, string Origin);
