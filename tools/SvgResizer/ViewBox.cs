// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;

namespace SvgResizer;

// Parsed SVG viewBox (min-x, min-y, width, height).
internal readonly struct ViewBox(double minX, double minY, double width, double height)
{
    private const double Epsilon = 1e-9;
    private static readonly char[] Separators = [' ', '\t', '\r', '\n', ','];

    public double MinX { get; } = minX;
    public double MinY { get; } = minY;
    public double Width { get; } = width;
    public double Height { get; } = height;

    public bool IsValid => Width > Epsilon && Height > Epsilon;

    public bool IsSquare => Math.Abs(Width - Height) <= Epsilon;

    // True when the origin is already at 0 0 (no min-x / min-y offset).
    public bool IsAtOrigin => Math.Abs(MinX) <= Epsilon && Math.Abs(MinY) <= Epsilon;

    // Parses a "min-x min-y width height" value. Accepts whitespace or comma
    // separators and any numeric format.
    public static bool TryParse(string value, out ViewBox viewBox)
    {
        viewBox = default;

        var parts = value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        var numbers = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        viewBox = new ViewBox(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    // Reconstructs the pre-hack viewBox from a squared (0 0 side side)
    // viewBox and the translation the old square-plus-translate hack applied.
    // Assumes the original viewBox had its origin at 0 0, which is the only
    // case a pure math reversal can recover without the actual original
    // bytes: the origin offset and the axis shrink are both folded into a
    // single translation value, so a non-zero original origin cannot be
    // separated back out from tx/ty alone. Use a real backup for icons whose
    // original viewBox did not start at 0 0.
    public static bool TryReconstructOriginal(ViewBox square, double tx, double ty, out ViewBox original)
    {
        var side = square.Width;
        var width = side - (2 * tx);
        var height = side - (2 * ty);

        if (width <= Epsilon || height <= Epsilon)
        {
            original = default;
            return false;
        }

        original = new ViewBox(0, 0, width, height);
        return true;
    }

    // Formats the viewBox as an attribute value, emitting whole numbers as
    // integers to keep the output clean.
    public string ToAttributeValue()
    {
        return string.Join(' ', Format(MinX), Format(MinY), Format(Width), Format(Height));
    }

    public static string Format(double value)
    {
        var rounded = Math.Round(value);
        if (Math.Abs(value - rounded) <= Epsilon)
        {
            return ((long)rounded).ToString(CultureInfo.InvariantCulture);
        }

        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
