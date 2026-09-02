// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SvgSquarer;

internal enum RestoreStatus
{
    Restored,
    AlreadyOriginal,
    NotSquared,
    Unrecognized
}

internal readonly record struct RestoreResult(RestoreStatus Status, string? Detail);

// Reverses SvgFileProcessor's squaring. It relies on that writer's exact,
// deterministic output shape - a <g transform="translate(tx,ty)"> wrapping
// the original content, immediately after the opening svg tag and just
// before the closing one - so plain text matching is enough. No XML parser
// is needed to pair up the wrapping tags, since this reverses a fixed layout
// this same tool produced, not arbitrary SVG structure.
internal static partial class SvgRestoreProcessor
{
    private static readonly Regex TranslateGroupOpen = TranslateGroupOpenRegex();

    public static RestoreResult Restore(string sourcePath, string targetPath, int? stripSize, bool dryRun)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var text = Utf8TextFile.Read(bytes, out var hasBom);

        var locateStatus = SvgRootTag.TryLocate(text, out var location, out var rawViewBoxValue);

        if (locateStatus != SvgRootTag.LocateStatus.Ok)
        {
            CopyThrough(targetPath, bytes, dryRun);
            var reason = locateStatus == SvgRootTag.LocateStatus.InvalidViewBox
                ? $"viewBox \"{rawViewBoxValue}\""
                : "no <svg> root element or viewBox";
            return new RestoreResult(RestoreStatus.Unrecognized, $"{reason} - copied as-is");
        }

        var viewBox = location.ViewBox;

        if (!viewBox.IsSquare || !viewBox.IsAtOrigin)
        {
            CopyThrough(targetPath, bytes, dryRun);
            return new RestoreResult(RestoreStatus.NotSquared, "viewBox is not squared at origin - copied as-is");
        }

        var contentStart = location.SvgTag.Index + location.SvgTag.Length;
        var groupMatch = TranslateGroupOpen.Match(text, contentStart);
        var closingSvgIndex = text.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);

        var wrapped =
            groupMatch.Success &&
            groupMatch.Index == contentStart &&
            closingSvgIndex >= 0 &&
            text.AsSpan(0, closingSvgIndex).TrimEnd().EndsWith("</g>", StringComparison.OrdinalIgnoreCase);

        if (!wrapped)
        {
            CopyThrough(targetPath, bytes, dryRun);
            return new RestoreResult(RestoreStatus.AlreadyOriginal, "already square, never wrapped - copied as-is");
        }

        var tx = double.Parse(groupMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var ty = double.Parse(groupMatch.Groups[2].Value, CultureInfo.InvariantCulture);

        if (!ViewBox.TryReconstructOriginal(viewBox, tx, ty, out var original))
        {
            CopyThrough(targetPath, bytes, dryRun);
            return new RestoreResult(RestoreStatus.Unrecognized, $"translate({tx},{ty}) does not yield a valid viewBox - copied as-is");
        }

        var groupEnd = groupMatch.Index + groupMatch.Length;
        var groupCloseIndex = text.LastIndexOf("</g>", closingSvgIndex, StringComparison.OrdinalIgnoreCase);
        var innerContent = text[groupEnd..groupCloseIndex];

        var newTag = SvgRootTag.ReplaceAttributeValue(location.SvgTag.Value, location.ViewBoxAttribute, original.ToAttributeValue());
        if (stripSize is { } size)
        {
            newTag = SvgRootTag.RemoveAttributeIfValueEquals(newTag, SvgRootTag.WidthAttribute, size);
            newTag = SvgRootTag.RemoveAttributeIfValueEquals(newTag, SvgRootTag.HeightAttribute, size);
        }

        var sb = new StringBuilder();
        sb.Append(text, 0, location.SvgTag.Index);
        sb.Append(newTag);
        sb.Append(innerContent);
        sb.Append(text, closingSvgIndex, text.Length - closingSvgIndex);

        var detail = $"{viewBox.ToAttributeValue()} -> {original.ToAttributeValue()}";

        if (!dryRun)
        {
            FileSystemHelper.EnsureDirectoryExists(targetPath);
            Utf8TextFile.Write(targetPath, sb.ToString(), hasBom);
        }

        return new RestoreResult(RestoreStatus.Restored, detail);
    }

    private static void CopyThrough(string targetPath, byte[] bytes, bool dryRun)
    {
        if (dryRun)
        {
            return;
        }

        FileSystemHelper.EnsureDirectoryExists(targetPath);
        File.WriteAllBytes(targetPath, bytes);
    }

    [GeneratedRegex(@"<g\s+transform\s*=\s*""translate\(\s*([+-]?[0-9.eE+-]+)\s*,\s*([+-]?[0-9.eE+-]+)\s*\)\s*""\s*>", RegexOptions.IgnoreCase, "")]
    private static partial Regex TranslateGroupOpenRegex();
}
