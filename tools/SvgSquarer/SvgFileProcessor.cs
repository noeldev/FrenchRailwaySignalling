// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;
using System.Text;

namespace SvgSquarer;

internal enum ProcessStatus
{
    Squared,
    AlreadySquare,
    NoViewBox,
    InvalidViewBox
}

internal readonly record struct ProcessResult(
    ProcessStatus Status,
    string? Detail,
    bool Rounded,
    bool Written);

// Reads a single SVG in place, squares its root viewBox if needed and
// optionally adds width/height, writing back while preserving the original
// encoding, byte order mark and line endings. Backs up the original bytes
// through backupStore right before any overwrite.
internal static class SvgFileProcessor
{
    public static ProcessResult Process(string path, string relativePath, int? size, BackupStore? backupStore, bool dryRun)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Utf8TextFile.Read(bytes, out var hasBom);

        var locateStatus = SvgRootTag.TryLocate(text, out var location, out var rawViewBoxValue);

        if (locateStatus == SvgRootTag.LocateStatus.NoSvgTag)
        {
            return new ProcessResult(ProcessStatus.NoViewBox, "no <svg> root element", false, false);
        }

        if (locateStatus == SvgRootTag.LocateStatus.NoViewBoxAttribute)
        {
            return new ProcessResult(ProcessStatus.NoViewBox, "missing viewBox attribute", false, false);
        }

        if (locateStatus == SvgRootTag.LocateStatus.InvalidViewBox)
        {
            return new ProcessResult(ProcessStatus.InvalidViewBox, $"viewBox \"{rawViewBoxValue}\"", false, false);
        }

        var viewBox = location.ViewBox;

        // Skip only when the viewBox is already square and starts at 0 0. A
        // square viewBox with a non-zero origin (for example "-2 -2 16 16") is
        // still rewritten so the origin is normalized to 0 0.
        if (viewBox.IsSquare && viewBox.IsAtOrigin)
        {
            var resizedTag = TryAddSize(location.SvgTag.Value, size, out var resized);
            if (!resized)
            {
                return new ProcessResult(ProcessStatus.AlreadySquare, viewBox.ToAttributeValue(), false, false);
            }

            if (!dryRun)
            {
                WriteResizedOnly(path, text, location, resizedTag, hasBom, backupStore, relativePath, bytes);
            }

            return new ProcessResult(ProcessStatus.AlreadySquare, $"{viewBox.ToAttributeValue()} (width/height added)", false, true);
        }

        var (square, tx, ty, rounded) = viewBox.ToSquaredCentered();
        var detail = $"{rawViewBoxValue} -> {square.ToAttributeValue()} (translate: {ViewBox.Format(tx)}, {ViewBox.Format(ty)})";

        if (!dryRun)
        {
            WriteSquared(path, text, location, square, tx, ty, size, hasBom, backupStore, relativePath, bytes);
        }

        return new ProcessResult(ProcessStatus.Squared, detail, rounded, true);
    }

    // Adds width/height to the root tag with the given size, but only when
    // neither attribute is already present - existing dimensions are never
    // overwritten.
    private static string TryAddSize(string svgTag, int? size, out bool resized)
    {
        resized = false;

        if (size is not { } value)
        {
            return svgTag;
        }

        if (SvgRootTag.WidthAttribute.IsMatch(svgTag) || SvgRootTag.HeightAttribute.IsMatch(svgTag))
        {
            return svgTag;
        }

        resized = true;
        var formatted = value.ToString(CultureInfo.InvariantCulture);
        return svgTag.Insert("<svg".Length, $" width=\"{formatted}\" height=\"{formatted}\"");
    }

    // Replaces the root viewBox value and wraps the original content in a
    // translate group so it stays centered, leaving the declaration, the other
    // attributes, the encoding and the line endings untouched.
    private static void WriteSquared(
        string path, string text, SvgRootLocation location, ViewBox square, double tx, double ty,
        int? size, bool hasBom, BackupStore? backupStore, string relativePath, byte[] originalBytes)
    {
        var newTag = SvgRootTag.ReplaceAttributeValue(location.SvgTag.Value, location.ViewBoxAttribute, square.ToAttributeValue());
        newTag = TryAddSize(newTag, size, out _);

        var translate = $"<g transform=\"translate({ViewBox.Format(tx)},{ViewBox.Format(ty)})\">";

        var closingSvgIndex = text.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (closingSvgIndex == -1)
        {
            closingSvgIndex = text.Length;
        }

        var contentStart = location.SvgTag.Index + location.SvgTag.Length;

        var sb = new StringBuilder();
        sb.Append(text, 0, location.SvgTag.Index);
        sb.Append(newTag);
        sb.Append(translate);
        sb.Append(text, contentStart, closingSvgIndex - contentStart);
        sb.Append("</g>");
        if (closingSvgIndex < text.Length)
        {
            sb.Append(text, closingSvgIndex, text.Length - closingSvgIndex);
        }

        backupStore?.BackupIfNeeded(relativePath, originalBytes, dryRun: false);
        Utf8TextFile.Write(path, sb.ToString(), hasBom);
    }

    // Rewrites the root tag only (width/height added), used when the viewBox
    // itself needs no change.
    private static void WriteResizedOnly(
        string path, string text, SvgRootLocation location, string resizedTag,
        bool hasBom, BackupStore? backupStore, string relativePath, byte[] originalBytes)
    {
        var newText = string.Concat(
            text.AsSpan(0, location.SvgTag.Index),
            resizedTag,
            text.AsSpan(location.SvgTag.Index + location.SvgTag.Length));

        backupStore?.BackupIfNeeded(relativePath, originalBytes, dryRun: false);
        Utf8TextFile.Write(path, newText, hasBom);
    }
}
