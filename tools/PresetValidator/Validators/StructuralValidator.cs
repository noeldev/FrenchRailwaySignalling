// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using PresetValidator.Core;

namespace PresetValidator.Validators;

// Content level consistency checks that go beyond what the schema can express:
// - items and groups must have a non empty name
// - combo and multiselect must keep values aligned with display_values and
//   short_descriptions (using the right default delimiter for each element)
// - preset_link must target an existing, unambiguous item name (JOSM resolves
//   the target by exact name match and silently picks the first on a duplicate)
// - the type attribute must only contain known object types
// - the same key should not be bound twice inside one item
public sealed class StructuralValidator : IValidator
{
    private static readonly HashSet<string> ValidTypes =
        ["node", "way", "closedway", "multipolygon", "relation", "area"];

    private static readonly HashSet<string> KeyedElements =
        ["key", "text", "combo", "multiselect", "check"];

    public string Name => "Structure";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var document = context.Document;

        var items = document.Descendants().Where(e => e.Name.LocalName == "item").ToList();

        // Item names can legitimately be duplicated (cosmetic labels); the count
        // only matters when a preset_link needs to resolve to exactly one item.
        var itemNameCounts = items
            .Select(e => e.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .GroupBy(name => name!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        CheckNames(document, issues);
        CheckOptionCounts(document, issues);
        CheckPresetLinks(document, itemNameCounts, issues);
        CheckTypes(document, issues);
        CheckDuplicateKeys(items, issues);

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private static void CheckNames(XDocument document, List<ValidationIssue> issues)
    {
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "item" or "group"))
        {
            if (string.IsNullOrWhiteSpace(element.Attribute("name")?.Value))
            {
                issues.Add(element.ToIssue(ValidationSeverity.Error, $"{element.Name.LocalName} without a name attribute."));
            }
        }
    }

    private static void CheckOptionCounts(XDocument document, List<ValidationIssue> issues)
    {
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName is "combo" or "multiselect"))
        {
            var values = element.Attribute("values")?.Value;
            if (values is null)
            {
                continue;
            }

            var isMultiselect = element.Name.LocalName == "multiselect";
            var delimiter = element.Attribute("delimiter")?.Value is { Length: > 0 } custom
                ? custom[0]
                : isMultiselect ? ';' : ',';

            var valueCount = values.Split(delimiter).Length;
            var key = element.Attribute("key")?.Value ?? "(unknown key)";

            CompareCount(element, "display_values", valueCount, delimiter, key, issues);
            CompareCount(element, "short_descriptions", valueCount, delimiter, key, issues);
        }
    }

    private static void CompareCount(XElement element, string attribute, int valueCount, char delimiter, string key, List<ValidationIssue> issues)
    {
        var raw = element.Attribute(attribute)?.Value;
        if (raw is null)
        {
            return;
        }

        var count = raw.Split(delimiter).Length;
        if (count != valueCount)
        {
            issues.Add(element.ToIssue(
                ValidationSeverity.Error,
                $"values ({valueCount}) and {attribute} ({count}) count mismatch for key '{key}'."));
        }
    }

    private static void CheckPresetLinks(XDocument document, IReadOnlyDictionary<string, int> itemNameCounts, List<ValidationIssue> issues)
    {
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "preset_link"))
        {
            var target = element.Attribute("preset_name")?.Value;
            if (string.IsNullOrEmpty(target))
            {
                issues.Add(element.ToIssue(ValidationSeverity.Error, "preset_link without a preset_name attribute."));
                continue;
            }

            var count = itemNameCounts.GetValueOrDefault(target, 0);
            if (count == 0)
            {
                issues.Add(element.ToIssue(
                    ValidationSeverity.Warning,
                    $"preset_link targets a preset not defined in this file: {target}"));
            }
            else if (count > 1)
            {
                issues.Add(element.ToIssue(
                    ValidationSeverity.Warning,
                    $"preset_link target '{target}' is ambiguous ({count} items share this name); JOSM resolves to the first one loaded."));
            }
        }
    }

    private static void CheckTypes(XDocument document, List<ValidationIssue> issues)
    {
        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "item" && e.Attribute("type") is not null))
        {
            var type = element.Attribute("type")!.Value;
            foreach (var token in type.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!ValidTypes.Contains(token))
                {
                    issues.Add(element.ToIssue(ValidationSeverity.Error, $"Unknown object type: {token}"));
                }
            }
        }
    }

    private static void CheckDuplicateKeys(IEnumerable<XElement> items, List<ValidationIssue> issues)
    {
        foreach (var item in items)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var keyed in item.Descendants().Where(e => KeyedElements.Contains(e.Name.LocalName)))
            {
                var key = keyed.Attribute("key")?.Value;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (!seen.Add(key))
                {
                    var name = item.Attribute("name")?.Value ?? "(unnamed)";
                    issues.Add(keyed.ToIssue(ValidationSeverity.Warning, $"Key '{key}' is used more than once in item '{name}'."));
                }
            }
        }
    }
}
