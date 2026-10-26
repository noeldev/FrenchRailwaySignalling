// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Presets;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Checks chunk definitions and references:
// - duplicate chunk ids
// - references pointing to an unknown chunk
// - chunks defined but never referenced (reported as a warning), except the
//   declaration-only chunks, whose id starts with the declaration prefix: they
//   declare tags that no item offers (see PresetParser), so referencing one is
//   reported instead
public sealed class ChunkValidator : IValidator
{
    public string Name => "Chunks and references";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();

        var definedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in context.Document.Descendants().Where(e => e.Name.LocalName == "chunk"))
        {
            var id = chunk.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id))
            {
                issues.Add(chunk.ToIssue(ValidationSeverity.Error, "Chunk without an id attribute."));
                continue;
            }

            if (!definedIds.Add(id))
            {
                issues.Add(chunk.ToIssue(ValidationSeverity.Error, $"Duplicate chunk id: {id}"));
            }
        }

        var referencedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in context.Document.Descendants().Where(e => e.Name.LocalName == "reference"))
        {
            var refId = reference.Attribute("ref")?.Value;
            if (string.IsNullOrEmpty(refId))
            {
                issues.Add(reference.ToIssue(ValidationSeverity.Error, "Reference without a ref attribute."));
                continue;
            }

            referencedIds.Add(refId);
            if (!definedIds.Contains(refId))
            {
                issues.Add(reference.ToIssue(ValidationSeverity.Error, $"Reference to an unknown chunk: {refId}"));
            }
        }

        foreach (var id in definedIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            var declaration = id.StartsWith(PresetParser.DeclarationChunkPrefix, StringComparison.Ordinal);
            if (declaration && referencedIds.Contains(id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"Declaration-only chunk referenced by an item: {id}"));
            }
            else if (!declaration && !referencedIds.Contains(id))
            {
                issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"Chunk defined but never referenced: {id}"));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
