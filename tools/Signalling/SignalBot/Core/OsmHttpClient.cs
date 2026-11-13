// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;

namespace SignalBot.Core;

// The HTTP clients of the program, one per service. The OSM servers and
// Overpass ask for an identifiable User-Agent.
public static class OsmHttpClient
{
    // The client of an OSM server. Once logged in it carries the access token
    // of the bot account: it must not be used for any other host.
    public static HttpClient Create(BotSettings settings, ServerSettings server)
    {
        var client = Create(settings, TimeSpan.FromSeconds(settings.Limits.RequestTimeoutSeconds));
        client.BaseAddress = new Uri(server.ApiUrl.TrimEnd('/') + "/");
        return client;
    }

    // The client of Overpass: it never carries the access token of an OSM
    // server, and each query sets its own time limit (see OverpassClient).
    public static HttpClient CreateOverpass(BotSettings settings) => Create(settings, Timeout.InfiniteTimeSpan);

    private static HttpClient Create(BotSettings settings, TimeSpan timeout)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Banner.ProductVersion()} (+{settings.Contact})");
        return client;
    }
}
