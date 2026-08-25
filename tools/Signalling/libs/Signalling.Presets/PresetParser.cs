// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;

namespace Signalling.Presets;

// Parses a JOSM tagging preset file and produces neutral PresetTag entries.
//
// Responsibilities:
//   - resolve <reference> elements against <chunk> definitions (recursively),
//   - flatten transparent containers (<optional>, <checkgroup>),
//   - emit one entry per fixed <key>, <text>, <check> and per <combo> /
//     <multiselect> value (both comma lists and <list_entry> children),
//   - emit entries from chunks that no item references, treating them as
//     declaration-only tags (added after the items, in traversal order).
//
// The parser no longer resolves icon URLs nor de-duplicates: it emits the raw
// relative icon path and preserves emission order so each consumer applies its
// own resolution and reconciliation. TagInfoGen resolves icons, de-duplicates
// and serializes; the sync source projects the entries into tag rules.
//
// Numeric-only combos (open-ended lists such as speeds) are emitted once as a
// key-only entry flagged IsOpenDomain, since their value domain is not closed.
//
// Description language is controlled by the useFrench flag.
public sealed class PresetParser
{
    private const string FrenchValuePrefix = "FR:";
    private const string EtcsValuePrefix = "ETCS:";
    private const string DefaultObjectType = "node";
    private const string DefaultItemName = "Signal";

    private readonly bool _useFrench;
    private readonly HashSet<string> _excludedKeys;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<XElement>> _chunks;

    public PresetParser(XDocument document, bool useFrench, IEnumerable<string> excludedKeys)
    {
        _useFrench = useFrench;
        _excludedKeys = new HashSet<string>(excludedKeys, StringComparer.Ordinal);
        _chunks = IndexChunks(document);
    }

    public IReadOnlyList<PresetTag> Parse(XDocument document)
    {
        var collected = new List<PresetTag>();

        foreach (var item in Descendants(document, "item"))
        {
            ProcessItem(item, collected);
        }

        // Chunks not referenced by any item are treated as declaration-only
        // tags. They are processed after the items, in traversal order, so a
        // consumer that keeps the first occurrence lets the more specific item
        // entries win.
        foreach (var chunk in UnreferencedChunks(document))
        {
            EmitFrom(chunk.Elements(), [DefaultObjectType], DefaultItemName, null, collected);
        }

        return collected;
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

    private void ProcessItem(XElement item, List<PresetTag> output)
    {
        var objectTypes = ReadObjectTypes(item);
        // The <label> is the full descriptive title; the item name is the menu
        // entry and is often abbreviated, so the label is preferred.
        var itemName = ItemLabel(item) ?? Localized(item, "name") ?? DefaultItemName;
        var itemIconPath = item.Attribute("icon")?.Value;

        EmitFrom(item.Elements(), objectTypes, itemName, itemIconPath, output);
    }

    // Emits entries from a sequence of elements (the children of an <item> or of
    // a declaration <chunk>). The item name and icon path are only used by fixed
    // keys carrying a signal-identity value; declaration chunks rely on their
    // <list_entry> descriptions and icons instead.
    private void EmitFrom(
        IEnumerable<XElement> elements,
        IReadOnlyList<string> objectTypes,
        string itemName,
        string? itemIconPath,
        List<PresetTag> output)
    {
        foreach (var element in Flatten(elements, new HashSet<string>(StringComparer.Ordinal)))
        {
            switch (element.Name.LocalName)
            {
                case "key":
                    EmitKey(element, objectTypes, itemName, itemIconPath, output);
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

    // Chunks that no <reference> points to. They never reach an item, so they
    // are emitted on their own as declaration-only tags.
    private static IEnumerable<XElement> UnreferencedChunks(XDocument document)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in Descendants(document, "reference"))
        {
            var id = reference.Attribute("ref")?.Value;
            if (!string.IsNullOrEmpty(id))
            {
                referenced.Add(id);
            }
        }

        return Descendants(document, "chunk").Where(chunk =>
        {
            var id = chunk.Attribute("id")?.Value;
            return !string.IsNullOrEmpty(id) && !referenced.Contains(id);
        });
    }

    // The localized text of the item's first <label> child, if any.
    private string? ItemLabel(XElement item)
    {
        var label = item.Elements().FirstOrDefault(e => e.Name.LocalName == "label");
        return label is null ? null : Localized(label, "text");
    }

    // Recursively resolves references and unwraps transparent containers. The
    // visited set guards against cyclic chunk references.
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
        string? itemIconPath,
        List<PresetTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        var value = NullIfEmpty(element.Attribute("value")?.Value);
        var isIdentity = IsSignalValue(value);

        output.Add(new PresetTag
        {
            Key = key,
            Value = value,
            ObjectTypes = objectTypes,
            // A signal-identity value takes the item name and icon; a generic
            // property value (form, height, ...) is declared bare.
            Description = isIdentity ? itemName : null,
            IconPath = isIdentity ? itemIconPath : null,
        });
    }

    private void EmitText(XElement element, IReadOnlyList<string> objectTypes, List<PresetTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        output.Add(new PresetTag
        {
            Key = key,
            Value = NullIfEmpty(element.Attribute("value")?.Value),
            ObjectTypes = objectTypes,
            Description = Localized(element, "text"),
        });
    }

    private void EmitCheck(XElement element, IReadOnlyList<string> objectTypes, List<PresetTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        output.Add(new PresetTag
        {
            Key = key,
            Value = NullIfEmpty(element.Attribute("value_on")?.Value) ?? "yes",
            ObjectTypes = objectTypes,
            Description = Localized(element, "text"),
        });
    }

    // Handles both the comma-separated "values" attribute and <list_entry>
    // children. Combos whose values are all numeric (open-ended lists such as
    // speeds) are declared once as a key-only entry flagged IsOpenDomain;
    // otherwise every value is enumerated with the icon declared in the preset.
    private void EmitChoiceValues(XElement element, IReadOnlyList<string> objectTypes, List<PresetTag> output)
    {
        var key = element.Attribute("key")?.Value;
        if (string.IsNullOrEmpty(key) || _excludedKeys.Contains(key))
        {
            return;
        }

        var comboIconPath = element.Attribute("icon")?.Value;

        if (HasOnlyNumericValues(element))
        {
            output.Add(new PresetTag
            {
                Key = key,
                Value = null,
                ObjectTypes = objectTypes,
                Description = Localized(element, "text"),
                IsOpenDomain = true,
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

                output.Add(new PresetTag
                {
                    Key = key,
                    Value = value,
                    ObjectTypes = objectTypes,
                    Description = NullIfEmpty(label) ?? value,
                    IconPath = comboIconPath,
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

            output.Add(new PresetTag
            {
                Key = key,
                Value = value,
                ObjectTypes = objectTypes,
                Description = NullIfEmpty(label) ?? value,
                IconPath = iconPath,
            });
        }
    }

    private IReadOnlyList<string> ReadObjectTypes(XElement item) // CA1859
    {
        var typeAttr = item.Attribute("type")?.Value ?? DefaultObjectType;
        var types = SplitList(typeAttr);
        return types.Count > 0 ? types : [DefaultObjectType];
    }

    // True when the combo has at least one value and every value is purely
    // numeric (covers open-ended numeric lists such as speeds, whatever the key).
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
