// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;

namespace TagInfoGen;

// Parses a JOSM tagging preset file and produces the list of taginfo tag entries.
//
// Responsibilities:
//   - resolve <reference> elements against <chunk> definitions (recursively),
//   - flatten transparent containers (<optional>, <checkgroup>),
//   - emit one tag entry per fixed <key>, <text>, <check> and per <combo> /
//     <multiselect> value (both comma lists and <list_entry> children),
//   - de-duplicate and sort the result.
//
// Icons are not discriminated: a fixed key carrying a signal-identity value
// (FR:* or ETCS:*) takes the item icon and name, every list value keeps the icon
// declared in the preset, and combos with only numeric values (speeds) are
// declared once as a key-only entry.
//
// Description language is controlled by the useFrenchDescriptions setting.
internal sealed class PresetParser
{
    private const string FrenchValuePrefix = "FR:";
    private const string EtcsValuePrefix = "ETCS:";
    private const string DefaultObjectType = "node";

    private readonly string _baseUrl;
    private readonly bool _useFrench;
    private readonly HashSet<string> _excludedKeys;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<XElement>> _chunks;

    public PresetParser(XDocument document, string baseUrl, bool useFrench, IEnumerable<string> excludedKeys)
    {
        _baseUrl = baseUrl;
        _useFrench = useFrench;
        _excludedKeys = new HashSet<string>(excludedKeys, StringComparer.Ordinal);
        _chunks = IndexChunks(document);
    }

    public IReadOnlyList<TagInfoTag> Parse(XDocument document)
    {
        var collected = new List<TagInfoTag>();

        foreach (var item in Descendants(document, "item"))
        {
            ProcessItem(item, collected);
        }

        return Deduplicate(collected);
    }

    // Builds a lookup of chunk id to its direct child elements.
    private static IReadOnlyDictionary<string, IReadOnlyList<XElement>> IndexChunks(XDocument document) // CA1859
    {
        var index = new Dictionary<string, IReadOnlyList<XElement>>(StringComparer.Ordinal);

        foreach (var chunk in Descendants(document, "chunk"))
        {
            string? id = chunk.Attribute("id")?.Value;
            if (!string.IsNullOrEmpty(id))
            {
                index[id] = chunk.Elements().ToList();
            }
        }

        return index;
    }

    private void ProcessItem(XElement item, List<TagInfoTag> output)
    {
        var objectTypes = ReadObjectTypes(item);
        // The <label> is the full descriptive title; the item name is the menu
        // entry and is often abbreviated, so the label is preferred.
        var itemName = ItemLabel(item) ?? Localized(item, "name") ?? "Signal";
        var itemIconUrl = IconResolver.Resolve(_baseUrl, item.Attribute("icon")?.Value);

        foreach (var element in Flatten(item.Elements(), new HashSet<string>(StringComparer.Ordinal)))
        {
            switch (element.Name.LocalName)
            {
                case "key":
                    EmitKey(element, objectTypes, itemName, itemIconUrl, output);
                    break;

                case "text":
                    EmitText(element, objectTypes, output);
                    break;

                case "combo":
                case "multiselect":
                    EmitChoiceValues(element, objectTypes, output);
                    break;

                case "check":
                    EmitCheck(element, objectTypes, output);
                    break;
            }
        }
    }

    // The localized text of the item's first <label> child, if any.
    private string? ItemLabel(XElement item)
    {
        var label = item.Elements().FirstOrDefault(e => e.Name.LocalName == "label");
        return label is null ? null : Localized(label, "text");
    }

    // Recursively resolves references and unwraps transparent containers.
    // The visited set guards against cyclic chunk references.
    private List<XElement> Flatten(IEnumerable<XElement> elements, ISet<string> visitedRefs)
    {
        var result = new List<XElement>();

        foreach (var element in elements)
        {
            switch (element.Name.LocalName)
            {
                case "reference":
                    var refId = element.Attribute("ref")?.Value;
                    if (!string.IsNullOrEmpty(refId)
                        && !visitedRefs.Contains(refId)
                        && _chunks.TryGetValue(refId, out var chunkElements))
                    {
                        visitedRefs.Add(refId);
                        result.AddRange(Flatten(chunkElements, visitedRefs));
                        visitedRefs.Remove(refId);
                    }
                    break;

                case "optional":
                case "checkgroup":
                    result.AddRange(Flatten(element.Elements(), visitedRefs));
                    break;

                default:
                    result.Add(element);
                    break;
            }
        }

        return result;
    }

    // A signal-identity value (FR:* or ETCS:*), as opposed to a generic property
    // value (form, position, ...). Only these carry the item icon and name.
    private static bool IsSignalValue(string? value)
    {
        var text = value ?? string.Empty;
        return text.StartsWith(FrenchValuePrefix, StringComparison.Ordinal)
            || text.StartsWith(EtcsValuePrefix, StringComparison.Ordinal);
    }

    private void EmitKey(
        XElement element,
        IReadOnlyList<string> objectTypes,
        string itemName,
        string? itemIconUrl,
        List<TagInfoTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        var value = NullIfEmpty(element.Attribute("value")?.Value);
        var isIdentity = IsSignalValue(value);

        output.Add(new TagInfoTag
        {
            Key = key,
            Value = value,
            ObjectTypes = objectTypes,
            // A signal-identity value takes the item name and icon; a generic
            // property value (form, height, ...) is declared bare.
            Description = isIdentity ? itemName : null,
            IconUrl = isIdentity ? itemIconUrl : null,
        });
    }

    private void EmitText(XElement element, IReadOnlyList<string> objectTypes, List<TagInfoTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        output.Add(new TagInfoTag
        {
            Key = key,
            Value = NullIfEmpty(element.Attribute("value")?.Value),
            ObjectTypes = objectTypes,
            Description = Localized(element, "text"),
        });
    }

    private void EmitCheck(XElement element, IReadOnlyList<string> objectTypes, List<TagInfoTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        output.Add(new TagInfoTag
        {
            Key = key,
            Value = NullIfEmpty(element.Attribute("value_on")?.Value) ?? "yes",
            ObjectTypes = objectTypes,
            Description = Localized(element, "text"),
        });
    }

    // Handles both the comma-separated "values" attribute and <list_entry> children.
    // Combos whose values are all numeric (open-ended lists such as speeds) are
    // declared once as a key-only entry; otherwise every value is enumerated with
    // the icon declared in the preset.
    private void EmitChoiceValues(XElement element, IReadOnlyList<string> objectTypes, List<TagInfoTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        var comboIconPath = element.Attribute("icon")?.Value;

        if (HasOnlyNumericValues(element))
        {
            output.Add(new TagInfoTag
            {
                Key = key,
                Value = null,
                ObjectTypes = objectTypes,
                Description = Localized(element, "text"),
                IconUrl = null,
            });
            return;
        }

        // Form A: comma-separated values with optional parallel display labels.
        var valuesAttr = element.Attribute("values")?.Value;
        if (!string.IsNullOrEmpty(valuesAttr))
        {
            var values = SplitList(valuesAttr);
            var labels = SplitNullable(LocalizedValue(element, "display_values"))
                                   ?? SplitNullable(LocalizedValue(element, "short_descriptions"));

            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                var label = labels is not null && i < labels.Count ? labels[i] : null;

                output.Add(new TagInfoTag
                {
                    Key = key,
                    Value = value,
                    ObjectTypes = objectTypes,
                    Description = NullIfEmpty(label) ?? value,
                    IconUrl = IconResolver.Resolve(_baseUrl, comboIconPath),
                });
            }
        }

        // Form B: explicit <list_entry> children.
        foreach (var entry in element.Elements().Where(e => e.Name.LocalName == "list_entry"))
        {
            var value = entry.Attribute("value")?.Value;
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var label = Localized(entry, "short_description") ?? Localized(entry, "display_value");
            var iconPath = entry.Attribute("icon")?.Value ?? comboIconPath;

            output.Add(new TagInfoTag
            {
                Key = key,
                Value = value,
                ObjectTypes = objectTypes,
                Description = NullIfEmpty(label) ?? value,
                IconUrl = IconResolver.Resolve(_baseUrl, iconPath),
            });
        }
    }

    // Merges duplicate entries (same key/value/object_types), keeping the richest
    // description and icon, then sorts by key and value for a stable output.
    private static IReadOnlyList<TagInfoTag> Deduplicate(IEnumerable<TagInfoTag> tags) // CA1859
    {
        var merged = new Dictionary<string, TagInfoTag>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            if (merged.TryGetValue(tag.DedupKey, out var existing))
            {
                existing.Description ??= tag.Description;
                existing.IconUrl ??= tag.IconUrl;
            }
            else
            {
                merged[tag.DedupKey] = tag;
            }
        }

        return [.. merged.Values
            .OrderBy(t => t.Key, StringComparer.Ordinal)
            .ThenBy(t => t.Value ?? string.Empty, StringComparer.Ordinal)];
    }

    private IReadOnlyList<string> ReadObjectTypes(XElement item) // CA1859
    {
        var typeAttr = item.Attribute("type")?.Value ?? DefaultObjectType;
        var types = SplitList(typeAttr);
        return types.Count > 0 ? types : [DefaultObjectType];
    }

    // True when the combo has at least one value and every value is purely numeric
    // (covers open-ended numeric lists such as speeds, regardless of the key name).
    private static bool HasOnlyNumericValues(XElement element)
    {
        var values = new List<string>();

        var valuesAttr = element.Attribute("values")?.Value;
        if (!string.IsNullOrEmpty(valuesAttr))
        {
            values.AddRange(SplitList(valuesAttr));
        }

        foreach (var entry in element.Elements().Where(e => e.Name.LocalName == "list_entry"))
        {
            var value = entry.Attribute("value")?.Value;
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        return values.Count > 0 && values.All(value => value.All(char.IsDigit));
    }

    // Reads an attribute, preferring the configured language with a fallback.
    private string? Localized(XElement element, string attributeName)
    {
        return NullIfEmpty(LocalizedValue(element, attributeName));
    }

    private string? LocalizedValue(XElement element, string attributeName)
    {
        var english = element.Attribute(attributeName)?.Value;
        var french = element.Attribute($"fr.{attributeName}")?.Value;

        return _useFrench ? (french ?? english) : (english ?? french);
    }

    private static List<string> SplitList(string value)
    {
        return [.. value
            .Split(',')
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)];
    }

    private static List<string>? SplitNullable(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value.Split(',').Select(item => item.Trim()).ToList();
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static IEnumerable<XElement> Descendants(XDocument document, string localName)
    {
        return document.Descendants().Where(e => e.Name.LocalName == localName);
    }
}
