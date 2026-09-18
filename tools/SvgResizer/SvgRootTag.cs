// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;

namespace SvgResizer;

internal readonly record struct SvgRootLocation(Match SvgTag, Match ViewBoxAttribute, ViewBox ViewBox);

// Locates the root <svg> element and reads/writes its attributes using a
// narrow text scan rather than a full XML parser, so the rest of the
// document - encoding, indentation, comments, quote style - is never
// touched. Shared by the sizing logic and the hack-removal logic.
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
    public static readonly Regex PreserveAspectRatioAttribute = PreserveAspectRatioAttributeRegex();

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

    // Returns an attribute's raw value if present on the tag, null otherwise.
    public static string? TryGetAttributeValue(string svgTag, Regex attributeRegex)
    {
        var match = attributeRegex.Match(svgTag);
        return match.Success ? match.Groups[3].Value : null;
    }

    // Replaces the attribute's value if present, or inserts it if absent -
    // used for width/height/preserveAspectRatio, which are always driven to
    // a single canonical value rather than merged with an existing one.
    public static string SetAttribute(string svgTag, Regex attributeRegex, string name, string value)
    {
        var match = attributeRegex.Match(svgTag);
        if (match.Success)
        {
            return ReplaceAttributeValue(svgTag, match, value);
        }

        return svgTag.Insert("<svg".Length, $" {name}=\"{value}\"");
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

    [GeneratedRegex(@"<svg\b[^>]*?>", RegexOptions.IgnoreCase, "")]
    private static partial Regex RootSvgTagRegex();

    [GeneratedRegex(@"(viewBox\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex ViewBoxAttributeRegex();

    [GeneratedRegex(@"(\bwidth\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex WidthAttributeRegex();

    [GeneratedRegex(@"(\bheight\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex HeightAttributeRegex();

    [GeneratedRegex(@"(\bpreserveAspectRatio\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex PreserveAspectRatioAttributeRegex();
}
