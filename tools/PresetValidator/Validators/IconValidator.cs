// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using PresetValidator.Core;

namespace PresetValidator.Validators;

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

            var resolution = ResolveCaseSensitive(iconRoot, iconPath);
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

    // Walks the path segment by segment, matching each one against the real
    // directory entries so the exact on-disk casing can be compared.
    private static IconResolution ResolveCaseSensitive(string root, string relativePath)
    {
        var segments = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        var currentDirectory = root;
        var actualSegments = new List<string>(segments.Length);

        for (var i = 0; i < segments.Length; i++)
        {
            if (!Directory.Exists(currentDirectory))
            {
                return IconResolution.Missing;
            }

            var isLast = i == segments.Length - 1;
            var candidates = isLast
                ? Directory.GetFileSystemEntries(currentDirectory)
                : Directory.GetDirectories(currentDirectory);

            var match = candidates
                .Select(entry => Path.GetFileName(entry))
                .FirstOrDefault(name => string.Equals(name, segments[i], StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                return IconResolution.Missing;
            }

            actualSegments.Add(match);
            currentDirectory = Path.Combine(currentDirectory, match);
        }

        var actualRelative = string.Join('/', actualSegments);
        var expectedRelative = string.Join('/', segments);

        return string.Equals(actualRelative, expectedRelative, StringComparison.Ordinal)
            ? IconResolution.Match
            : IconResolution.Mismatch(actualRelative);
    }

    private enum IconResolutionStatus
    {
        Match,
        Missing,
        CaseMismatch
    }

    private readonly record struct IconResolution(IconResolutionStatus Status, string? ActualRelativePath)
    {
        public static IconResolution Match => new(IconResolutionStatus.Match, null);

        public static IconResolution Missing => new(IconResolutionStatus.Missing, null);

        public static IconResolution Mismatch(string actual) => new(IconResolutionStatus.CaseMismatch, actual);
    }
}
