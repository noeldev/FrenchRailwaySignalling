// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;
using System.Text.RegularExpressions;

namespace SvgSquarer;

internal enum SquareStatus
{
    Squared,
    Unsquared,
    AlreadySquare,
    NotSquared,
    NoViewBox,
    InvalidViewBox,
    NoContent,
    InvalidGroup
}

internal readonly record struct SquareResult(SquareStatus Status, string Text, string Detail);

// Turns an SVG into a square document for JOSM, which aligns non-square
// icons to the left of their slot and ignores both the viewBox origin and
// preserveAspectRatio. The viewBox becomes 0 0 side side, side being the
// longest dimension, and the original content is wrapped in a translate group
// that centers it. The group carries id="josm-square" so it can always be
// told apart from the artwork and removed again by Unsquare. A square viewBox
// is left as is, so squaring twice is harmless. width, height and
// preserveAspectRatio are dropped from the root tag in every case: the
// viewBox alone gives the size. Pure text in, text out.
internal static partial class IconSquarer
{
    private const string ClosingTag = "</svg>";
    private const string GroupClosingTag = "</g>";
    private const string GroupId = "josm-square";

    private static readonly Regex SquareGroupOpen = SquareGroupOpenRegex();

    public static SquareResult Square(string text)
    {
        if (!TryLocate(text, out var location, out var failure))
        {
            return failure;
        }

        var svgTag = location.SvgTag.Value;
        var tagStart = location.SvgTag.Index;
        var tagEnd = tagStart + location.SvgTag.Length;
        var viewBox = location.ViewBox;

        var newTag = SvgRootTag.RemoveAttribute(svgTag, SvgRootTag.WidthAttribute);
        newTag = SvgRootTag.RemoveAttribute(newTag, SvgRootTag.HeightAttribute);
        newTag = SvgRootTag.RemoveAttribute(newTag, SvgRootTag.PreserveAspectRatioAttribute);

        if (viewBox.IsSquare)
        {
            var squareText = $"{text[..tagStart]}{newTag}{text[tagEnd..]}";
            return new SquareResult(SquareStatus.AlreadySquare, squareText, $"square viewBox {viewBox.ToAttributeValue()}");
        }

        var closingIndex = text.LastIndexOf(ClosingTag, StringComparison.OrdinalIgnoreCase);
        if (svgTag.EndsWith("/>", StringComparison.Ordinal) || closingIndex < tagEnd)
        {
            return new SquareResult(SquareStatus.NoContent, text, "no content between <svg> and </svg>");
        }

        var side = Math.Max(viewBox.Width, viewBox.Height);
        var tx = ((side - viewBox.Width) / 2) - viewBox.MinX;
        var ty = ((side - viewBox.Height) / 2) - viewBox.MinY;

        var squareViewBox = new ViewBox(0, 0, side, side).ToAttributeValue();
        newTag = SvgRootTag.ReplaceAttributeValue(newTag, SvgRootTag.ViewBoxAttribute, squareViewBox);

        var translate = $"translate({ViewBox.Format(tx)},{ViewBox.Format(ty)})";

        var squaredText =
            $"{text[..tagStart]}{newTag}<g id=\"{GroupId}\" transform=\"{translate}\">" +
            $"{text[tagEnd..closingIndex]}{GroupClosingTag}{text[closingIndex..]}";

        return new SquareResult(SquareStatus.Squared, squaredText, $"viewBox {viewBox.ToAttributeValue()} -> {squareViewBox}, {translate}");
    }

    // Reverses Square: removes the josm-square group and restores the
    // viewBox it replaced. The longer axis is recovered exactly from the
    // translation; the shorter one is assumed to start at 0, which holds for
    // every icon squared from a viewBox at the origin.
    public static SquareResult Unsquare(string text)
    {
        if (!TryLocate(text, out var location, out var failure))
        {
            return failure;
        }

        var tagStart = location.SvgTag.Index;
        var tagEnd = tagStart + location.SvgTag.Length;
        var viewBox = location.ViewBox;

        var groupMatch = SquareGroupOpen.Match(text, tagEnd);
        if (!groupMatch.Success || !text.AsSpan(tagEnd, groupMatch.Index - tagEnd).IsWhiteSpace())
        {
            return new SquareResult(SquareStatus.NotSquared, text, $"no {GroupId} group, viewBox {viewBox.ToAttributeValue()}");
        }

        var closingIndex = text.LastIndexOf(ClosingTag, StringComparison.OrdinalIgnoreCase);
        var groupCloseIndex = closingIndex < 0 ? -1 : text.LastIndexOf(GroupClosingTag, closingIndex, StringComparison.OrdinalIgnoreCase);
        var groupEnd = groupMatch.Index + groupMatch.Length;

        if (!viewBox.IsSquare || groupCloseIndex < groupEnd ||
            !text.AsSpan(groupCloseIndex + GroupClosingTag.Length, closingIndex - groupCloseIndex - GroupClosingTag.Length).IsWhiteSpace())
        {
            return new SquareResult(SquareStatus.InvalidGroup, text, $"{GroupId} group found but the document is not in the shape Square writes");
        }

        var tx = double.Parse(groupMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var ty = double.Parse(groupMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        var side = viewBox.Width;

        // The padded axis has a positive offset; the other one carries the
        // original origin, negated.
        var original = tx > 0
            ? new ViewBox(0, -ty, side - (2 * tx), side)
            : new ViewBox(-tx, 0, side, side - (2 * ty));

        if (!original.IsValid)
        {
            return new SquareResult(SquareStatus.InvalidGroup, text, $"translate({groupMatch.Groups[1].Value},{groupMatch.Groups[2].Value}) does not fit viewBox {viewBox.ToAttributeValue()}");
        }

        var newTag = SvgRootTag.ReplaceAttributeValue(location.SvgTag.Value, SvgRootTag.ViewBoxAttribute, original.ToAttributeValue());

        var unsquaredText =
            $"{text[..tagStart]}{newTag}{text[groupEnd..groupCloseIndex]}{text[closingIndex..]}";

        return new SquareResult(SquareStatus.Unsquared, unsquaredText, $"viewBox {viewBox.ToAttributeValue()} -> {original.ToAttributeValue()}");
    }

    private static bool TryLocate(string text, out SvgRootLocation location, out SquareResult failure)
    {
        var locateStatus = SvgRootTag.TryLocate(text, out location, out var rawViewBoxValue);

        failure = locateStatus switch
        {
            SvgRootTag.LocateStatus.NoSvgTag => new SquareResult(SquareStatus.NoViewBox, text, "no <svg> root element"),
            SvgRootTag.LocateStatus.NoViewBoxAttribute => new SquareResult(SquareStatus.NoViewBox, text, "missing viewBox attribute"),
            SvgRootTag.LocateStatus.InvalidViewBox => new SquareResult(SquareStatus.InvalidViewBox, text, $"viewBox \"{rawViewBoxValue}\""),
            _ => default
        };

        return locateStatus == SvgRootTag.LocateStatus.Ok;
    }

    [GeneratedRegex(@"<g\s+id\s*=\s*""josm-square""\s+transform\s*=\s*""translate\(\s*([+-]?(?:\d+\.?\d*|\.\d+))\s*[,\s]\s*([+-]?(?:\d+\.?\d*|\.\d+))\s*\)""\s*>", RegexOptions.IgnoreCase, "")]
    private static partial Regex SquareGroupOpenRegex();
}
