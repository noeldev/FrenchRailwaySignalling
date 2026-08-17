// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using PresetValidator.Core;

namespace PresetValidator.Validators;

// Enforces the preset rule that every item must carry a wiki link. The link is
// often provided through a referenced chunk rather than inline, so a naive per
// item scan would raise false positives; this validator resolves chunk
// references (including chunk to chunk) before deciding an item has no link.
public sealed class ItemLinkValidator : IValidator
{
    public string Name => "Item wiki links";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();

        var chunks = context.Document.Descendants()
            .Where(e => e.Name.LocalName == "chunk" && e.Attribute("id") is not null)
            .ToDictionary(e => e.Attribute("id")!.Value, e => e, StringComparer.Ordinal);

        var linkCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var item in context.Document.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            if (!HasReachableLink(item, chunks, linkCache, []))
            {
                var name = item.Attribute("name")?.Value ?? "(unnamed)";
                issues.Add(item.ToIssue(
                    ValidationSeverity.Warning,
                    $"Item '{name}' has no wiki link, directly or through a referenced chunk."));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    // Returns true when the element contains a link directly or through any of
    // the chunks it references, following chunk to chunk references as needed.
    private static bool HasReachableLink(
        XElement element,
        IReadOnlyDictionary<string, XElement> chunks,
        Dictionary<string, bool> linkCache,
        HashSet<string> visiting)
    {
        if (element.Descendants().Any(e => e.Name.LocalName == "link"))
        {
            return true;
        }

        foreach (var reference in element.Descendants().Where(e => e.Name.LocalName == "reference"))
        {
            var refId = reference.Attribute("ref")?.Value;
            if (string.IsNullOrEmpty(refId) || !chunks.TryGetValue(refId, out var chunk))
            {
                continue;
            }

            if (ChunkHasLink(refId, chunk, chunks, linkCache, visiting))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ChunkHasLink(
        string chunkId,
        XElement chunk,
        IReadOnlyDictionary<string, XElement> chunks,
        Dictionary<string, bool> linkCache,
        HashSet<string> visiting)
    {
        if (linkCache.TryGetValue(chunkId, out var cached))
        {
            return cached;
        }

        // Guard against a reference cycle: treat it as no link found.
        if (!visiting.Add(chunkId))
        {
            return false;
        }

        var result = HasReachableLink(chunk, chunks, linkCache, visiting);
        visiting.Remove(chunkId);
        linkCache[chunkId] = result;
        return result;
    }
}
