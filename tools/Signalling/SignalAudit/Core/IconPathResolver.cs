// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalAudit.Core;

// Resolves an icon path relative to a root directory, matching each path
// segment against the real directory entries so the exact on-disk casing can
// be recovered. Shared by every validator that needs to open or check an
// icon file, since a preset entry may reference a path with different casing
// than the file that actually exists (which passes on Windows but breaks on
// case sensitive file systems, like the ones serving JOSM presets).
public static class IconPathResolver
{
    public static IconResolution Resolve(string root, string relativePath)
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
                .Select(Path.GetFileName)
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
            ? IconResolution.Match(currentDirectory)
            : IconResolution.Mismatch(actualRelative, currentDirectory);
    }
}

// Status of an icon path lookup against the real file system.
public enum IconResolutionStatus
{
    Match,
    Missing,
    CaseMismatch
}

// FullPath is the resolved absolute path when the icon was found (Match or
// CaseMismatch), and null when it could not be located at all.
public readonly record struct IconResolution(IconResolutionStatus Status, string? ActualRelativePath, string? FullPath)
{
    public static IconResolution Match(string fullPath) => new(IconResolutionStatus.Match, null, fullPath);

    public static IconResolution Missing => new(IconResolutionStatus.Missing, null, null);

    public static IconResolution Mismatch(string actualRelativePath, string fullPath) =>
        new(IconResolutionStatus.CaseMismatch, actualRelativePath, fullPath);
}
