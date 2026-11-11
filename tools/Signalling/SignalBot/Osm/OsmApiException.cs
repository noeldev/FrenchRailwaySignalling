// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;

namespace SignalBot.Osm;

// A request the OSM server refused, with its answer.
public sealed class OsmApiException(HttpStatusCode status, string response, TimeSpan? retryAfter)
    : Exception($"The OSM server answered {(int)status} {status}: {response.Trim()}")
{
    public HttpStatusCode Status { get; } = status;

    // Wait asked by the server when it limits the edit rate.
    public TimeSpan? RetryAfter { get; } = retryAfter;

    // A node changed since it was read (or was deleted): the upload was
    // refused as a whole and nothing was written.
    public bool IsConflict => Status is HttpStatusCode.Conflict or HttpStatusCode.Gone or HttpStatusCode.PreconditionFailed;

    public bool IsRateLimited => Status == HttpStatusCode.TooManyRequests;

    public bool IsTransient => (int)Status >= 500 || IsRateLimited;
}
