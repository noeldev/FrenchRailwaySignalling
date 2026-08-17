// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using PresetValidator.Core;

namespace PresetValidator.Validators;

// Detects items that end up under the exact same menu path (the chain of
// enclosing group names plus the item name). JOSM shows them as duplicate
// entries, which is almost always an oversight. Duplicate item names on their
// own are fine and common, so only a full path collision is reported, and as a
// warning rather than an error.
public sealed class MenuPathValidator : IValidator
{
    public string Name => "Menu paths";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var byPath = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);

        foreach (var item in context.Document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            // Items defined inside a chunk are templates, not menu leaves.
            if (item.Ancestors().Any(a => a.Name.LocalName == "chunk"))
            {
                continue;
            }

            var path = BuildPath(item);
            if (path is null)
            {
                continue;
            }

            if (!byPath.TryGetValue(path, out var list))
            {
                list = [];
                byPath[path] = list;
            }

            list.Add(item);
        }

        var issues = new List<ValidationIssue>();
        foreach (var (path, items) in byPath)
        {
            if (items.Count <= 1)
            {
                continue;
            }

            foreach (var item in items)
            {
                issues.Add(item.ToIssue(
                    ValidationSeverity.Warning,
                    $"Duplicate menu path ({items.Count} items): {path}"));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    // Joins the enclosing group names and the item name. Returns null when a
    // name is missing, since the structural validator already reports that.
    private static string? BuildPath(XElement item)
    {
        var itemName = item.Attribute("name")?.Value;
        if (string.IsNullOrEmpty(itemName))
        {
            return null;
        }

        var groups = item.Ancestors()
            .Where(a => a.Name.LocalName == "group")
            .Select(a => a.Attribute("name")?.Value ?? "?")
            .Reverse();

        return string.Join(" / ", groups.Append(itemName));
    }
}
