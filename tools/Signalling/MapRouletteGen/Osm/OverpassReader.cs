// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;

namespace MapRouletteGen.Osm;

// Reads the nodes of an Overpass export, in either format offered by
// overpass-turbo:
// - raw data ([out:json] ... out body;): an "elements" array;
// - GeoJSON: a FeatureCollection whose features carry the element reference
//   in "@id" (node/123) and the tags as properties. Properties starting with
//   "@" are overpass-turbo metadata, not tags.
// Other elements (ways, relations, areas) are ignored.
public static class OverpassReader
{
    private const string NodePrefix = "node/";

    public static IReadOnlyList<OsmNode> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        if (root.TryGetProperty("elements", out var elements))
        {
            return ReadElements(elements);
        }

        if (root.TryGetProperty("features", out var features))
        {
            return ReadFeatures(features);
        }

        throw new InvalidDataException($"Not an Overpass export (no \"elements\" or \"features\" array): {path}");
    }

    private static List<OsmNode> ReadElements(JsonElement elements)
    {
        List<OsmNode> nodes = [];
        foreach (var element in elements.EnumerateArray())
        {
            if (element.GetProperty("type").GetString() != "node")
            {
                continue;
            }

            var tags = element.TryGetProperty("tags", out var tagElement) ? ReadTags(tagElement) : [];
            nodes.Add(new OsmNode(
                element.GetProperty("id").GetInt64(),
                element.GetProperty("lat").GetDouble(),
                element.GetProperty("lon").GetDouble(),
                tags));
        }

        return nodes;
    }

    private static List<OsmNode> ReadFeatures(JsonElement features)
    {
        List<OsmNode> nodes = [];
        foreach (var feature in features.EnumerateArray())
        {
            if (!feature.TryGetProperty("properties", out var properties)
                || !properties.TryGetProperty("@id", out var reference)
                || reference.GetString() is not { } id
                || !id.StartsWith(NodePrefix, StringComparison.Ordinal)
                || feature.GetProperty("geometry").GetProperty("type").GetString() != "Point")
            {
                continue;
            }

            var coordinates = feature.GetProperty("geometry").GetProperty("coordinates");
            nodes.Add(new OsmNode(
                long.Parse(id[NodePrefix.Length..]),
                coordinates[1].GetDouble(),
                coordinates[0].GetDouble(),
                ReadTags(properties)));
        }

        return nodes;
    }

    private static Dictionary<string, string> ReadTags(JsonElement tags)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (var tag in tags.EnumerateObject().Where(tag => !tag.Name.StartsWith('@') && tag.Value.ValueKind == JsonValueKind.String))
        {
            result[tag.Name] = tag.Value.GetString()!;
        }

        return result;
    }
}
