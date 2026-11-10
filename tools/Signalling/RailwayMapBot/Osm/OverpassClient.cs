// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;

namespace RailwayMapBot.Osm;

// Runs the configured query for one area. Overpass only lists the candidates:
// every node is read again from the OSM server before it is edited.
public sealed class OverpassClient(HttpClient client, OverpassSettings settings, LimitSettings limits)
{
    private const string AreaPlaceholder = "{area}";

    public async Task<IReadOnlyList<OsmNode>> QueryAsync(string areaCode, CancellationToken cancellationToken)
    {
        var query = string.Join('\n', settings.Query).Replace(AreaPlaceholder, areaCode, StringComparison.Ordinal);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent([KeyValuePair.Create("data", query)]);
                using var response = await client.PostAsync(settings.Endpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return OverpassReader.Read(stream);
            }
            catch (Exception ex) when (attempt < limits.ReadRetries
                && ex is HttpRequestException or TaskCanceledException or InvalidDataException
                && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"  Overpass query failed ({ex.Message}), trying again in {limits.RetryDelaySeconds} s...");
                await Task.Delay(TimeSpan.FromSeconds(limits.RetryDelaySeconds), cancellationToken);
            }
        }
    }
}
