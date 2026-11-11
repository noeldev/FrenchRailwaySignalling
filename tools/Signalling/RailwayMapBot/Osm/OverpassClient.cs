// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;

namespace RailwayMapBot.Osm;

// Runs the configured query for one area. Overpass only lists the candidates:
// every node is read again from the OSM server before it is edited. The answer
// can be saved as received, to be read again with --input.
public sealed class OverpassClient(HttpClient client, OverpassSettings settings, LimitSettings limits)
{
    private const string AreaPlaceholder = "{area}";

    public async Task<OverpassAnswer> QueryAsync(string areaSelector, string? savePath, CancellationToken cancellationToken)
    {
        var query = string.Join('\n', settings.Query).Replace(AreaPlaceholder, areaSelector, StringComparison.Ordinal);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var content = new FormUrlEncodedContent([KeyValuePair.Create("data", query)]);
                using var response = await client.PostAsync(settings.Endpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                var answer = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var result = OverpassReader.Read(new MemoryStream(answer, writable: false));
                if (savePath is not null)
                {
                    await File.WriteAllBytesAsync(savePath, answer, cancellationToken);
                }

                return result;
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
