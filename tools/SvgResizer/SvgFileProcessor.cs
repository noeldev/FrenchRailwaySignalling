// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;

namespace SvgResizer;

internal enum ProcessStatus
{
    Resized,
    AlreadyCorrect,
    SizeMismatch,
    NoViewBox,
    InvalidViewBox
}

internal readonly record struct ProcessResult(ProcessStatus Status, string? Detail);

// Reads a single SVG in place. Removes the older square-viewBox-plus-
// translate-group hack if present, restoring the original viewBox, then sets
// width/height/preserveAspectRatio without ever touching the viewBox or
// content otherwise. Backs up the original bytes through backupStore right
// before any overwrite.
internal static class SvgFileProcessor
{
    private const string CanonicalPreserveAspectRatio = "xMidYMid meet";

    public static ProcessResult Process(
        string path, string relativePath, int? width, int? height, bool keepAspectRatio,
        bool force, BackupStore? backupStore, bool dryRun)
    {
        var bytes = File.ReadAllBytes(path);
        var text = Utf8TextFile.Read(bytes, out var hasBom);

        var locateStatus = SvgRootTag.TryLocate(text, out var location, out var rawViewBoxValue);

        if (locateStatus == SvgRootTag.LocateStatus.NoSvgTag)
        {
            return new ProcessResult(ProcessStatus.NoViewBox, "no <svg> root element");
        }

        if (locateStatus == SvgRootTag.LocateStatus.NoViewBoxAttribute)
        {
            return new ProcessResult(ProcessStatus.NoViewBox, "missing viewBox attribute");
        }

        if (locateStatus == SvgRootTag.LocateStatus.InvalidViewBox)
        {
            return new ProcessResult(ProcessStatus.InvalidViewBox, $"viewBox \"{rawViewBoxValue}\"");
        }

        string? unhackDetail = null;

        if (SquareHackRemover.TryRemove(text, location, out var unwrappedText, out var restoredViewBox))
        {
            unhackDetail = $"removed square/translate hack, viewBox restored to {restoredViewBox.ToAttributeValue()}";
            text = unwrappedText;

            // Positions shifted after unwrapping - re-locate on the new text.
            locateStatus = SvgRootTag.TryLocate(text, out location, out rawViewBoxValue);
            if (locateStatus != SvgRootTag.LocateStatus.Ok)
            {
                return new ProcessResult(ProcessStatus.InvalidViewBox, "viewBox became invalid after removing the hack");
            }
        }

        var (targetWidth, targetHeight) = ComputeTargetSize(location.ViewBox, width, height, keepAspectRatio);

        var svgTag = location.SvgTag.Value;
        var widthRaw = SvgRootTag.TryGetAttributeValue(svgTag, SvgRootTag.WidthAttribute);
        var heightRaw = SvgRootTag.TryGetAttributeValue(svgTag, SvgRootTag.HeightAttribute);
        var aspectRaw = SvgRootTag.TryGetAttributeValue(svgTag, SvgRootTag.PreserveAspectRatioAttribute);

        var widthOk = ValueEquals(widthRaw, targetWidth);
        var heightOk = ValueEquals(heightRaw, targetHeight);
        var aspectOk = aspectRaw == CanonicalPreserveAspectRatio;
        var conforms = widthOk && heightOk && aspectOk;
        var anyPresent = widthRaw != null || heightRaw != null || aspectRaw != null;

        var formattedWidth = ViewBox.Format(targetWidth);
        var formattedHeight = ViewBox.Format(targetHeight);

        if (conforms)
        {
            if (unhackDetail == null)
            {
                return new ProcessResult(ProcessStatus.AlreadyCorrect, $"width/height already {formattedWidth}x{formattedHeight}, preserveAspectRatio already set");
            }

            if (!dryRun)
            {
                backupStore?.BackupIfNeeded(relativePath, bytes, dryRun: false);
                Utf8TextFile.Write(path, text, hasBom);
            }

            return new ProcessResult(ProcessStatus.Resized, unhackDetail);
        }

        if (anyPresent && !force)
        {
            return new ProcessResult(
                ProcessStatus.SizeMismatch,
                $"width=\"{widthRaw}\" height=\"{heightRaw}\" preserveAspectRatio=\"{aspectRaw}\" does not match target {formattedWidth}x{formattedHeight} - use --force to override");
        }

        var newTag = SvgRootTag.SetAttribute(svgTag, SvgRootTag.WidthAttribute, "width", formattedWidth);
        newTag = SvgRootTag.SetAttribute(newTag, SvgRootTag.HeightAttribute, "height", formattedHeight);
        newTag = SvgRootTag.SetAttribute(newTag, SvgRootTag.PreserveAspectRatioAttribute, "preserveAspectRatio", CanonicalPreserveAspectRatio);

        var newText = string.Concat(
            text.AsSpan(0, location.SvgTag.Index),
            newTag,
            text.AsSpan(location.SvgTag.Index + location.SvgTag.Length));

        var detail = unhackDetail != null
            ? $"{unhackDetail}; width/height set to {formattedWidth}x{formattedHeight}"
            : $"width/height set to {formattedWidth}x{formattedHeight}";

        if (!dryRun)
        {
            backupStore?.BackupIfNeeded(relativePath, bytes, dryRun: false);
            Utf8TextFile.Write(path, newText, hasBom);
        }

        return new ProcessResult(ProcessStatus.Resized, detail);
    }

    // Determines the target width/height for this file. When both --width and
    // --height are given, they are used exactly as given and --keep-aspect-
    // ratio has nothing left to derive. When only one is given, the other is
    // either equal to it (square, the default) or computed from the current
    // viewBox's own aspect ratio (--keep-aspect-ratio).
    private static (double Width, double Height) ComputeTargetSize(ViewBox viewBox, int? width, int? height, bool keepAspectRatio)
    {
        if (width is { } w && height is { } h)
        {
            return (w, h);
        }

        if (width is { } wOnly)
        {
            return keepAspectRatio
                ? (wOnly, wOnly * viewBox.Height / viewBox.Width)
                : (wOnly, wOnly);
        }

        var hOnly = height!.Value;
        return keepAspectRatio
            ? (hOnly * viewBox.Width / viewBox.Height, hOnly)
            : (hOnly, hOnly);
    }

    private static bool ValueEquals(string? raw, double expected)
    {
        return raw != null &&
               double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
               Math.Abs(value - expected) <= 1e-6;
    }
}
