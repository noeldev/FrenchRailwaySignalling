// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using System.Globalization;
using System.Text.RegularExpressions;

namespace SvgSquarer;

internal readonly record struct SvgRootLocation(Match SvgTag, Match ViewBoxAttribute, ViewBox ViewBox);

// Locates the root <svg> element and its viewBox using a narrow text scan
// rather than a full XML parser, so the rest of the document - encoding,
// indentation, comments, quote style - is never touched. Shared by the
// squaring and restore processors so the two never drift apart.
internal static partial class SvgRootTag
{
    internal enum LocateStatus
    {
        Ok,
        NoSvgTag,
        NoViewBoxAttribute,
        InvalidViewBox
    }

    private static readonly Regex RootSvgTag = RootSvgTagRegex();
    private static readonly Regex ViewBoxAttribute = ViewBoxAttributeRegex();
    public static readonly Regex WidthAttribute = WidthAttributeRegex();
    public static readonly Regex HeightAttribute = HeightAttributeRegex();

    public static LocateStatus TryLocate(string text, out SvgRootLocation location, out string? rawViewBoxValue)
    {
        location = default;
        rawViewBoxValue = null;

        var svgTag = RootSvgTag.Match(text);
        if (!svgTag.Success)
        {
            return LocateStatus.NoSvgTag;
        }

        var attribute = ViewBoxAttribute.Match(svgTag.Value);
        if (!attribute.Success)
        {
            return LocateStatus.NoViewBoxAttribute;
        }

        rawViewBoxValue = attribute.Groups[3].Value;

        if (!ViewBox.TryParse(rawViewBoxValue, out var viewBox) || !viewBox.IsValid)
        {
            return LocateStatus.InvalidViewBox;
        }

        location = new SvgRootLocation(svgTag, attribute, viewBox);
        return LocateStatus.Ok;
    }

    // Rebuilds a root tag with one attribute's value replaced, keeping the
    // quote style and every other attribute untouched.
    public static string ReplaceAttributeValue(string svgTag, Match attribute, string newValue)
    {
        var prefix = attribute.Groups[1].Value;
        var quote = attribute.Groups[2].Value;
        var replacement = $"{prefix}{quote}{newValue}{quote}";

        return string.Concat(
            svgTag.AsSpan(0, attribute.Index),
            replacement,
            svgTag.AsSpan(attribute.Index + attribute.Length));
    }

    // Removes a width/height attribute if present and its numeric value
    // equals expectedValue - used by restore to undo a --size that was only
    // added because the attribute was missing in the first place.
    public static string RemoveAttributeIfValueEquals(string svgTag, Regex attributeRegex, int expectedValue)
    {
        var match = attributeRegex.Match(svgTag);
        if (!match.Success)
        {
            return svgTag;
        }

        if (!double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            Math.Abs(value - expectedValue) > 1e-9)
        {
            return svgTag;
        }

        return svgTag.Remove(match.Index, match.Length);
    }

    [GeneratedRegex(@"<svg\b[^>]*?>", RegexOptions.IgnoreCase, "")]
    private static partial Regex RootSvgTagRegex();

    [GeneratedRegex(@"(viewBox\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex ViewBoxAttributeRegex();

    [GeneratedRegex(@"(\bwidth\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex WidthAttributeRegex();

    [GeneratedRegex(@"(\bheight\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex HeightAttributeRegex();
}
