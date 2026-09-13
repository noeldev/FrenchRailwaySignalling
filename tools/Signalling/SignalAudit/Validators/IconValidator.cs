// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Verifies that every referenced icon exists on disk and that the stored path
// matches the real path casing. Case mismatches pass on Windows but break on
// case sensitive file systems, which is where JOSM presets are served from
// (Linux, GitHub Pages).
public sealed class IconValidator : IValidator
{
    public string Name => "Icons";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var iconRoot = context.Options.IconRoot;

        // A single icon can be referenced many times; resolve each distinct path once.
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in context.Document.Descendants().Where(HasLocalIcon))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var iconPath = element.Attribute("icon")!.Value;
            if (!reported.Add(iconPath))
            {
                continue;
            }

            var resolution = IconPathResolver.Resolve(iconRoot, iconPath);
            switch (resolution.Status)
            {
                case IconResolutionStatus.Missing:
                    issues.Add(element.ToIssue(ValidationSeverity.Error, $"Icon file not found: {iconPath}"));
                    break;

                case IconResolutionStatus.CaseMismatch:
                    issues.Add(element.ToIssue(
                        ValidationSeverity.Error,
                        $"Icon path case mismatch: '{iconPath}' should be '{resolution.ActualRelativePath}'."));
                    break;
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private static bool HasLocalIcon(XElement element)
    {
        var icon = element.Attribute("icon")?.Value;
        return !string.IsNullOrWhiteSpace(icon) && !IsRemote(icon);
    }

    private static bool IsRemote(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
