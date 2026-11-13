// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Osm;

// A query Overpass did not answer, or an answer that cannot be trusted. A
// transient failure (timeout, load, rate limit, truncated answer) is worth
// another try; the others (a query Overpass refuses) are not.
public sealed class OverpassException(string message, bool isTransient, TimeSpan? retryAfter = null) : Exception(message)
{
    public bool IsTransient { get; } = isTransient;

    // Wait asked by the server, when it gave one.
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
