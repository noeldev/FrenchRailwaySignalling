// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;

namespace SignalBot.Core;

// The HTTP client of a server. The OSM servers and Overpass ask for an
// identifiable User-Agent.
public static class OsmHttpClient
{
    public static HttpClient Create(BotSettings settings, ServerSettings server)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri(server.ApiUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(settings.Limits.RequestTimeoutSeconds),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Banner.ProductVersion()} (+{settings.Contact})");
        return client;
    }
}
