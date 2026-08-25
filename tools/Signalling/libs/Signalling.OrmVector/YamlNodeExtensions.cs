// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using YamlDotNet.RepresentationModel;

namespace Signalling.OrmVector;

// Small helpers to read the YAML representation model by key, and to pull scalar
// or sequence values without repeating the node-type checks at every call site.
internal static class YamlNodeExtensions
{
    // The child value mapped to the given scalar key, or null when absent.
    public static YamlNode? Child(this YamlMappingNode node, string key)
    {
        foreach (var pair in node.Children)
        {
            if (pair.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                return pair.Value;
            }
        }

        return null;
    }

    // The scalar value mapped to the given key, or null when absent or not scalar.
    public static string? ScalarValue(this YamlMappingNode node, string key) =>
        node.Child(key) is YamlScalarNode scalar ? scalar.Value : null;

    // The scalar items of a sequence node, or empty when the node is not a
    // sequence. Null entries are normalized to an empty string.
    public static IReadOnlyList<string> Scalars(YamlNode? node) =>
        node is YamlSequenceNode sequence
            ? [.. sequence.Children.OfType<YamlScalarNode>().Select(scalar => scalar.Value ?? string.Empty)]
            : [];
}
