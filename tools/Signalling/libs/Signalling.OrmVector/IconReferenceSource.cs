// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using YamlDotNet.RepresentationModel;

namespace Signalling.OrmVector;

// Reads the icon paths an ORM-vector YAML feature declares, for the configured
// country. This is deliberately separate from OrmVectorSource: that reader
// treats an icon case's value as the icon path only to skip it, since coverage
// comparison cares about the tag match, not the file; this reader is the
// mirror image, walking the same "icon" structure but keeping only the paths.
// The feature-level "exampleIcon" (legend icon) is read as a literal path too.
public sealed class IconReferenceSource(string country = "FR")
{
    public string Country { get; } = country;

    public IReadOnlyList<IconReference> ReadReferences(Stream content)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(content, leaveOpen: true))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return [];
        }

        var references = new List<IconReference>();
        if (root.Child("features") is YamlSequenceNode features)
        {
            foreach (var feature in features.Children.OfType<YamlMappingNode>())
            {
                if (!string.Equals(feature.ScalarValue("country"), Country, StringComparison.Ordinal))
                {
                    continue;
                }

                var origin = feature.ScalarValue("description") ?? "(feature)";

                var exampleIcon = feature.ScalarValue("exampleIcon");
                if (exampleIcon is not null)
                {
                    references.Add(new IconReference(exampleIcon, null, null, origin));
                }

                ReadIconValues(feature, origin, references);
            }
        }

        return references;
    }

    private static void ReadIconValues(YamlMappingNode feature, string origin, List<IconReference> references)
    {
        if (feature.Child("icon") is not YamlSequenceNode icon)
        {
            return;
        }

        foreach (var component in icon.Children.OfType<YamlMappingNode>())
        {
            var defaultValue = component.ScalarValue("default");
            if (defaultValue is not null)
            {
                references.Add(new IconReference(defaultValue, null, null, origin));
            }

            if (component.Child("cases") is not YamlSequenceNode cases)
            {
                continue;
            }

            foreach (var caseNode in cases.Children.OfType<YamlMappingNode>())
            {
                var value = caseNode.ScalarValue("value");
                if (value is null)
                {
                    continue;
                }

                references.Add(new IconReference(
                    value,
                    caseNode.ScalarValue("regex"),
                    caseNode.ScalarValue("example"),
                    origin));
            }
        }
    }
}
