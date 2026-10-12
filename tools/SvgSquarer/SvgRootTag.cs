// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;

namespace SvgSquarer;

internal readonly record struct SvgRootLocation(Match SvgTag, ViewBox ViewBox);

// Locates the root <svg> element and reads/writes its attributes using a
// narrow text scan rather than a full XML parser, so the rest of the
// document - encoding, indentation, comments, quote style - is never
// touched.
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
    public static readonly Regex ViewBoxAttribute = ViewBoxAttributeRegex();
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

        rawViewBoxValue = TryGetAttributeValue(svgTag.Value, ViewBoxAttribute);
        if (rawViewBoxValue == null)
        {
            return LocateStatus.NoViewBoxAttribute;
        }

        if (!ViewBox.TryParse(rawViewBoxValue, out var viewBox) || !viewBox.IsValid)
        {
            return LocateStatus.InvalidViewBox;
        }

        location = new SvgRootLocation(svgTag, viewBox);
        return LocateStatus.Ok;
    }

    // Returns an attribute's raw value if present on the tag, null otherwise.
    public static string? TryGetAttributeValue(string svgTag, Regex attributeRegex)
    {
        var match = attributeRegex.Match(svgTag);
        return match.Success ? match.Groups[3].Value : null;
    }

    // Removes an attribute and the whitespace in front of it. Returns the tag
    // unchanged when the attribute is absent.
    public static string RemoveAttribute(string svgTag, Regex attributeRegex)
    {
        var match = attributeRegex.Match(svgTag);
        if (!match.Success)
        {
            return svgTag;
        }

        var start = match.Index;
        while (start > 0 && char.IsWhiteSpace(svgTag[start - 1]))
        {
            start--;
        }

        return svgTag.Remove(start, match.Index + match.Length - start);
    }

    // Replaces an existing attribute's value, keeping its quote style and
    // every other attribute untouched. Returns the tag unchanged when the
    // attribute is absent.
    public static string ReplaceAttributeValue(string svgTag, Regex attributeRegex, string newValue)
    {
        var match = attributeRegex.Match(svgTag);
        if (!match.Success)
        {
            return svgTag;
        }

        var prefix = match.Groups[1].Value;
        var quote = match.Groups[2].Value;

        return string.Concat(
            svgTag.AsSpan(0, match.Index),
            $"{prefix}{quote}{newValue}{quote}",
            svgTag.AsSpan(match.Index + match.Length));
    }

    // The lookbehinds keep an attribute name from matching inside another
    // one: \b alone would accept the "width" of stroke-width, since "-" is a
    // word boundary.
    [GeneratedRegex(@"<svg\b[^>]*?>", RegexOptions.IgnoreCase, "")]
    private static partial Regex RootSvgTagRegex();

    [GeneratedRegex(@"((?<![\w:.-])viewBox\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex ViewBoxAttributeRegex();

    [GeneratedRegex(@"((?<![\w:.-])width\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex WidthAttributeRegex();

    [GeneratedRegex(@"((?<![\w:.-])height\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex HeightAttributeRegex();

    [GeneratedRegex(@"((?<![\w:.-])preserveAspectRatio\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex PreserveAspectRatioAttributeRegex();
}
