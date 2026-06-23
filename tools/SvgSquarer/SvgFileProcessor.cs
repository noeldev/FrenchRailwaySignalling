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
    string Path,
    ProcessStatus Status,
    string? Detail,
    bool Rounded);

// Reads a single SVG, squares its root viewBox if needed and writes it back
// while preserving the original encoding, byte order mark and line endings.
internal static partial class SvgFileProcessor
{
    private const double Epsilon = 1e-9;
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    private static readonly Regex RootSvgTag = RootSvgTagRegex();
    private static readonly Regex ViewBoxAttribute = ViewBoxAttributeRegex();

    public static ProcessResult Process(string sourcePath, string targetPath, bool dryRun)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        var hasBom = HasUtf8Bom(bytes);
        var text = new UTF8Encoding(false).GetString(bytes, hasBom ? Utf8Bom.Length : 0,
            bytes.Length - (hasBom ? Utf8Bom.Length : 0));

        var svgTag = RootSvgTag.Match(text);
        if (!svgTag.Success)
        {
            return new ProcessResult(sourcePath, ProcessStatus.NoViewBox, "no <svg> root element", false);
        }

        var attribute = ViewBoxAttribute.Match(svgTag.Value);
        if (!attribute.Success)
        {
            return new ProcessResult(sourcePath, ProcessStatus.NoViewBox, "missing viewBox attribute", false);
        }

        var rawValue = attribute.Groups[3].Value;
        if (!ViewBox.TryParse(rawValue, out ViewBox viewBox) || !viewBox.IsValid)
        {
            return new ProcessResult(sourcePath, ProcessStatus.InvalidViewBox, $"viewBox \"{rawValue}\"", false);
        }

        // Détection automatique : l'icône a-t-elle une origine min-x ou min-y non nulle ?
        var hasNonZeroOrigin = Math.Abs(viewBox.MinX) > Epsilon || Math.Abs(viewBox.MinY) > Epsilon;

        // On ne saute l'icône que si elle est carrée ET que son origine est déjà propre à 0 0
        if (viewBox.IsSquare && !hasNonZeroOrigin)
        {
            if (!dryRun && sourcePath != targetPath)
            {
                EnsureDirectoryExists(targetPath);
                File.WriteAllBytes(targetPath, bytes);
            }
            return new ProcessResult(sourcePath, ProcessStatus.AlreadySquare, viewBox.ToAttributeValue(), false);
        }

        // Si l'icône est déjà carrée mais avec une mauvaise origine (ex: -2 -2 16 16), 
        // ToSquaredCentered va renvoyer un viewBox="0 0 16 16" et calculer translate(2,2).
        var squared = viewBox.ToSquaredCentered(out double tx, out double ty, out bool rounded);
        var detail = $"{rawValue} -> {squared.ToAttributeValue()} (translate: {ViewBox.Format(tx)}, {ViewBox.Format(ty)})";

        if (!dryRun)
        {
            var newTag = ReplaceViewBoxValue(svgTag.Value, attribute, squared.ToAttributeValue());

            var closingSvgIndex = text.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
            if (closingSvgIndex == -1)
            {
                closingSvgIndex = text.Length;
            }

            var sb = new StringBuilder();
            sb.Append(text, 0, svgTag.Index);
            sb.Append(newTag);
            sb.Append($"<g transform=\"translate({ViewBox.Format(tx)},{ViewBox.Format(ty)})\">");
            sb.Append(text, svgTag.Index + svgTag.Length, closingSvgIndex - (svgTag.Index + svgTag.Length));

            if (closingSvgIndex < text.Length)
            {
                sb.Append("</g>");
                sb.Append(text, closingSvgIndex, text.Length - closingSvgIndex);
            }
            else
            {
                sb.Append("</g>");
            }

            EnsureDirectoryExists(targetPath);
            File.WriteAllText(targetPath, sb.ToString(), new UTF8Encoding(hasBom));
        }

        return new ProcessResult(sourcePath, ProcessStatus.Squared, detail, rounded);
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

    private static bool HasUtf8Bom(byte[] bytes)
    {
        return bytes.Length >= Utf8Bom.Length &&
               bytes[0] == Utf8Bom[0] &&
               bytes[1] == Utf8Bom[1] &&
               bytes[2] == Utf8Bom[2];
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    [GeneratedRegex(@"<svg\b[^>]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline, "fr-FR")]
    private static partial Regex RootSvgTagRegex();

    [GeneratedRegex(@"(viewBox\s*=\s*)(""|')(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline, "fr-FR")]
    private static partial Regex ViewBoxAttributeRegex();
}