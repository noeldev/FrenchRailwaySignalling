// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;

namespace PresetValidator.Core;

// Central factory for HttpClient instances so every network check shares the
// same decompression settings and an identifiable User-Agent.
public static class HttpClientFactory
{
    private const string UserAgent = "PresetValidator/1.0 (JOSM preset checker)";

    public static HttpClient Create(int timeoutSeconds)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}
