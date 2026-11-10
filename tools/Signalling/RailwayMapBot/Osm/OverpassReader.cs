// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;

namespace RailwayMapBot.Osm;

// Reads the nodes of an Overpass answer or export in raw data format
// ([out:json] ... out meta;). Without the metadata (out body) the nodes have
// version 0, which only an offline plan accepts. Other elements are ignored.
public static class OverpassReader
{
    public static IReadOnlyList<OsmNode> Read(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("elements", out var elements))
        {
            // Overpass reports a failed query (timeout, load) in a "remark".
            throw new InvalidDataException("Not an Overpass raw data answer (no \"elements\" array).");
        }

        if (document.RootElement.TryGetProperty("remark", out var remark))
        {
            throw new InvalidDataException($"Overpass did not complete the query: {remark.GetString()}");
        }

        List<OsmNode> nodes = [];
        foreach (var element in elements.EnumerateArray().Where(element => element.GetProperty("type").GetString() == "node"))
        {
            nodes.Add(new OsmNode(
                element.GetProperty("id").GetInt64(),
                element.TryGetProperty("version", out var version) ? version.GetInt32() : 0,
                Visible: true,
                element.GetProperty("lat").GetDouble(),
                element.GetProperty("lon").GetDouble(),
                element.TryGetProperty("timestamp", out var timestamp) ? timestamp.GetDateTimeOffset() : DateTimeOffset.UnixEpoch,
                element.TryGetProperty("user", out var user) ? user.GetString() ?? string.Empty : string.Empty,
                element.TryGetProperty("tags", out var tags) ? ReadTags(tags) : new Dictionary<string, string>()));
        }

        return nodes;
    }

    private static Dictionary<string, string> ReadTags(JsonElement tags) =>
        tags.EnumerateObject()
            .Where(tag => tag.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(tag => tag.Name, tag => tag.Value.GetString()!, StringComparer.Ordinal);
}
