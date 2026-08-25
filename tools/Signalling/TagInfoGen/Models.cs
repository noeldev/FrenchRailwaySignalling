// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json.Serialization;

namespace TagInfoGen;

// Data transfer objects mirroring the taginfo project file format, version 1.
// See https://wiki.openstreetmap.org/wiki/Taginfo/Projects for the specification.
// Null properties are omitted on serialization, matching the "optional field" rules.

internal sealed class TagInfoFile
{
    [JsonPropertyName("data_format")]
    [JsonPropertyOrder(0)]
    public int DataFormat { get; init; } = 1;

    [JsonPropertyName("data_url")]
    [JsonPropertyOrder(1)]
    public string? DataUrl { get; init; }

    [JsonPropertyName("project")]
    [JsonPropertyOrder(2)]
    public required TagInfoProject Project { get; init; }

    [JsonPropertyName("tags")]
    [JsonPropertyOrder(3)]
    public required IReadOnlyList<TagInfoTag> Tags { get; init; }
}

internal sealed class TagInfoProject
{
    [JsonPropertyName("name")]
    [JsonPropertyOrder(0)]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonPropertyOrder(1)]
    public required string Description { get; init; }

    [JsonPropertyName("project_url")]
    [JsonPropertyOrder(2)]
    public required string ProjectUrl { get; init; }

    [JsonPropertyName("doc_url")]
    [JsonPropertyOrder(3)]
    public string? DocUrl { get; init; }

    [JsonPropertyName("icon_url")]
    [JsonPropertyOrder(4)]
    public string? IconUrl { get; init; }

    [JsonPropertyName("contact_name")]
    [JsonPropertyOrder(5)]
    public required string ContactName { get; init; }

    [JsonPropertyName("contact_email")]
    [JsonPropertyOrder(6)]
    public required string ContactEmail { get; init; }
}

internal sealed class TagInfoTag
{
    [JsonPropertyName("key")]
    [JsonPropertyOrder(0)]
    public required string Key { get; init; }

    [JsonPropertyName("value")]
    [JsonPropertyOrder(1)]
    public string? Value { get; set; }

    [JsonPropertyName("object_types")]
    [JsonPropertyOrder(2)]
    public required IReadOnlyList<string> ObjectTypes { get; init; }

    [JsonPropertyName("description")]
    [JsonPropertyOrder(3)]
    public string? Description { get; set; }

    [JsonPropertyName("icon_url")]
    [JsonPropertyOrder(4)]
    public string? IconUrl { get; set; }

    // Identity used for de-duplication. Two tags are the same taginfo entry when
    // their key, value and object_types all match. Not part of the output format.
    [JsonIgnore]
    public string DedupKey => $"{Key}\u0001{Value}\u0001{string.Join(',', ObjectTypes)}";
}
