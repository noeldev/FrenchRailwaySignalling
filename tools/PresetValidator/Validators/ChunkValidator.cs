// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using PresetValidator.Core;

namespace PresetValidator.Validators;

// Checks chunk definitions and references:
// - duplicate chunk ids
// - references pointing to an unknown chunk
// - chunks defined but never referenced (reported as a warning)
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

        foreach (var unused in definedIds.Except(referencedIds).OrderBy(id => id, StringComparer.Ordinal))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"Chunk defined but never referenced: {unused}"));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }
}
