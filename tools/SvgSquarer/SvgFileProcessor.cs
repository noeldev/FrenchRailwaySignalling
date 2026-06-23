// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Text.RegularExpressions;

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
    bool Rounded);

// Reads a single SVG, squares its root viewBox if needed and writes it back
// while preserving the original encoding, byte order mark and line endings.
internal static partial class SvgFileProcessor
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly Regex RootSvgTag = RootSvgTagRegex();
    private static readonly Regex ViewBoxAttribute = ViewBoxAttributeRegex();

    public static ProcessResult Process(string sourcePath, string targetPath, bool dryRun)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var bomLength = hasBom ? Encoding.UTF8.Preamble.Length : 0;
        var text = Utf8NoBom.GetString(bytes, bomLength, bytes.Length - bomLength);

        var svgTag = RootSvgTag.Match(text);
        if (!svgTag.Success)
        {
            return new ProcessResult(ProcessStatus.NoViewBox, "no <svg> root element", false);
        }

        var attribute = ViewBoxAttribute.Match(svgTag.Value);
        if (!attribute.Success)
        {
            return new ProcessResult(ProcessStatus.NoViewBox, "missing viewBox attribute", false);
        }

        var rawValue = attribute.Groups[3].Value;
        if (!ViewBox.TryParse(rawValue, out var viewBox) || !viewBox.IsValid)
        {
            return new ProcessResult(ProcessStatus.InvalidViewBox, $"viewBox \"{rawValue}\"", false);
        }

        // Skip only when the viewBox is already square and starts at 0 0. A
        // square viewBox with a non-zero origin (for example "-2 -2 16 16") is
        // still rewritten so the origin is normalized to 0 0.
        if (viewBox.IsSquare && viewBox.IsAtOrigin)
        {
            if (!dryRun && sourcePath != targetPath)
            {
                EnsureDirectoryExists(targetPath);
                File.WriteAllBytes(targetPath, bytes);
            }
            return new ProcessResult(ProcessStatus.AlreadySquare, viewBox.ToAttributeValue(), false);
        }

        var (square, tx, ty, rounded) = viewBox.ToSquaredCentered();
        var detail = $"{rawValue} -> {square.ToAttributeValue()} (translate: {ViewBox.Format(tx)}, {ViewBox.Format(ty)})";

        if (!dryRun)
        {
            WriteSquared(targetPath, text, svgTag, attribute, square, tx, ty, hasBom);
        }

        return new ProcessResult(ProcessStatus.Squared, detail, rounded);
    }

    // Replaces the root viewBox value and wraps the original content in a
    // translate group so it stays centered, leaving the declaration, the other
    // attributes, the encoding and the line endings untouched.
    private static void WriteSquared(
        string targetPath, string text, Match svgTag, Match attribute,
        ViewBox square, double tx, double ty, bool hasBom)
    {
        var newTag = ReplaceViewBoxValue(svgTag.Value, attribute, square.ToAttributeValue());
        var translate = $"<g transform=\"translate({ViewBox.Format(tx)},{ViewBox.Format(ty)})\">";

        var closingSvgIndex = text.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (closingSvgIndex == -1)
        {
            closingSvgIndex = text.Length;
        }

        var contentStart = svgTag.Index + svgTag.Length;

        var sb = new StringBuilder();
        sb.Append(text, 0, svgTag.Index);
        sb.Append(newTag);
        sb.Append(translate);
        sb.Append(text, contentStart, closingSvgIndex - contentStart);
        sb.Append("</g>");
        if (closingSvgIndex < text.Length)
        {
            sb.Append(text, closingSvgIndex, text.Length - closingSvgIndex);
        }

        EnsureDirectoryExists(targetPath);
        File.WriteAllText(targetPath, sb.ToString(), new UTF8Encoding(hasBom));
    }

    // Rebuilds the opening svg tag with the new viewBox value, leaving the
    // quote style and every other attribute untouched.
    private static string ReplaceViewBoxValue(string svgTag, Match attribute, string newValue)
    {
        var prefix = attribute.Groups[1].Value;
        var quote = attribute.Groups[2].Value;
        var replacement = $"{prefix}{quote}{newValue}{quote}";

        return string.Concat(
            svgTag.AsSpan(0, attribute.Index),
            replacement,
            svgTag.AsSpan(attribute.Index + attribute.Length));
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    [GeneratedRegex(@"<svg\b[^>]*?>", RegexOptions.IgnoreCase, "")]
    private static partial Regex RootSvgTagRegex();

    [GeneratedRegex(@"(viewBox\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline, "")]
    private static partial Regex ViewBoxAttributeRegex();
}
