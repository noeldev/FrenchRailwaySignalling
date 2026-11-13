// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;

namespace SignalBot.Osm;

// Runs the configured query for one area, with an optional mapper filter
// (the nodes whose last version is by one of the given mappers, see
// UserFilter and UidFilter). Overpass only lists the candidates:
// every node is read again from the OSM server before it is edited. The answer
// can be saved as received, to be read again with --input.
public sealed class OverpassClient(HttpClient client, OverpassSettings settings, LimitSettings limits)
{
    public const string AreaPlaceholder = "{area}";
    public const string MapperPlaceholder = "{mapper}";

    public async Task<OverpassAnswer> QueryAsync(string areaSelector, string mapperFilter, string? savePath, CancellationToken cancellationToken)
    {
        var query = string.Join('\n', settings.Query)
            .Replace(AreaPlaceholder, areaSelector, StringComparison.Ordinal)
            .Replace(MapperPlaceholder, mapperFilter, StringComparison.Ordinal);

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

    // Overpass filter on the display name of the user of the last version;
    // quotes and backslashes are escaped in the name.
    public static string UserFilter(string name) =>
        $"(user:\"{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\")";

    // Overpass filter on the uid of the user of the last version.
    public static string UidFilter(IEnumerable<long> uids) => $"(uid:{string.Join(',', uids)})";
}
