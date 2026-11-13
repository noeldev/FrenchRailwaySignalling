// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;

namespace SignalBot.Osm;

// Reads the nodes of an Overpass answer or export in raw data format
// ([out:json] ... out meta;). Without the metadata (out body) the nodes have
// version 0, which only an offline plan accepts. The name of the area comes
// from an area element, when the query outputs one (.scope out tags;). Other
// elements are ignored.
// An answer is all or nothing: Overpass answers a query it could not complete
// (timeout, memory) with the elements found so far and a "remark", and a
// connection cut off leaves truncated JSON. Both are refused as a whole, so
// a partial list is never taken for the list.
public static class OverpassReader
{
    public static OverpassAnswer Read(Stream stream)
    {
        try
        {
            using var document = JsonDocument.Parse(stream);
            return Read(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new OverpassException($"the answer is not complete Overpass raw data ({ex.Message})", isTransient: true);
        }
    }

    private static OverpassAnswer Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("elements", out var elements))
        {
            throw new OverpassException("the answer is not Overpass raw data (no \"elements\" array)", isTransient: true);
        }

        if (root.TryGetProperty("remark", out var remark))
        {
            throw new OverpassException($"the query was not completed ({remark.GetString()})", isTransient: true);
        }

        List<OsmNode> nodes = [];
        string? areaName = null;
        foreach (var element in elements.EnumerateArray())
        {
            var type = element.GetProperty("type").GetString();
            if (type == "area" && areaName is null && element.TryGetProperty("tags", out var areaTags) && areaTags.TryGetProperty("name", out var name))
            {
                areaName = name.GetString();
            }

            if (type != "node")
            {
                continue;
            }

            nodes.Add(new OsmNode(
                element.GetProperty("id").GetInt64(),
                element.TryGetProperty("version", out var version) ? version.GetInt32() : 0,
                Visible: true,
                element.GetProperty("lat").GetDouble(),
                element.GetProperty("lon").GetDouble(),
                element.TryGetProperty("timestamp", out var timestamp) ? timestamp.GetDateTimeOffset() : DateTimeOffset.UnixEpoch,
                element.TryGetProperty("user", out var user) ? user.GetString() ?? string.Empty : string.Empty,
                element.TryGetProperty("tags", out var tags) ? ReadTags(tags) : new Dictionary<string, string>(),
                element.TryGetProperty("uid", out var uid) ? uid.GetInt64() : 0));
        }

        return new OverpassAnswer(nodes, areaName, ReadDataTime(root));
    }

    // "osm3s": { "timestamp_osm_base": "2026-11-13T21:58:12Z" }
    private static DateTimeOffset? ReadDataTime(JsonElement root) =>
        root.TryGetProperty("osm3s", out var osm3s)
        && osm3s.TryGetProperty("timestamp_osm_base", out var time)
        && time.TryGetDateTimeOffset(out var value)
            ? value
            : null;

    private static Dictionary<string, string> ReadTags(JsonElement tags) =>
        tags.EnumerateObject()
            .Where(tag => tag.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(tag => tag.Name, tag => tag.Value.GetString()!, StringComparer.Ordinal);
}
