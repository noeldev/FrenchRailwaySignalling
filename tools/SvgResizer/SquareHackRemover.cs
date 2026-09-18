// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SvgResizer;

// Detects and reverses the square-viewBox-plus-translate-group hack this
// tool used to apply, before that approach was replaced by width/height plus
// preserveAspectRatio. Pure text in, text out: relies on that old writer's
// exact, deterministic output shape - a <g transform="translate(tx,ty)">
// wrapping the original content, immediately after the opening svg tag and
// just before the closing one - so plain text matching is enough. No XML
// parser is needed to pair up the wrapping tags, since this reverses a fixed
// layout this same tool used to produce, not arbitrary SVG structure.
internal static partial class SquareHackRemover
{
    private static readonly Regex TranslateGroupOpen = TranslateGroupOpenRegex();

    // Returns true and the unwrapped text plus the restored viewBox when the
    // hack is found; returns false and leaves unwrappedText/restoredViewBox
    // at their default values otherwise. The reconstructed viewBox assumes
    // the pre-hack origin was 0 0 - see ViewBox.TryReconstructOriginal.
    public static bool TryRemove(string text, SvgRootLocation location, out string unwrappedText, out ViewBox restoredViewBox)
    {
        unwrappedText = text;
        restoredViewBox = default;

        var viewBox = location.ViewBox;
        if (!viewBox.IsSquare || !viewBox.IsAtOrigin)
        {
            return false;
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
            return false;
        }

        var tx = double.Parse(groupMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var ty = double.Parse(groupMatch.Groups[2].Value, CultureInfo.InvariantCulture);

        if (!ViewBox.TryReconstructOriginal(viewBox, tx, ty, out var original))
        {
            return false;
        }

        var groupEnd = groupMatch.Index + groupMatch.Length;
        var groupCloseIndex = text.LastIndexOf("</g>", closingSvgIndex, StringComparison.OrdinalIgnoreCase);
        var innerContent = text[groupEnd..groupCloseIndex];

        var newTag = SvgRootTag.ReplaceAttributeValue(location.SvgTag.Value, location.ViewBoxAttribute, original.ToAttributeValue());

        var sb = new StringBuilder();
        sb.Append(text, 0, location.SvgTag.Index);
        sb.Append(newTag);
        sb.Append(innerContent);
        sb.Append(text, closingSvgIndex, text.Length - closingSvgIndex);

        unwrappedText = sb.ToString();
        restoredViewBox = original;
        return true;
    }

    [GeneratedRegex(@"<g\s+transform\s*=\s*""translate\(\s*([+-]?[0-9.eE+-]+)\s*,\s*([+-]?[0-9.eE+-]+)\s*\)\s*""\s*>", RegexOptions.IgnoreCase, "")]
    private static partial Regex TranslateGroupOpenRegex();
}
