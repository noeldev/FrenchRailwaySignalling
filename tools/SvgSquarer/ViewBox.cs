// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;

namespace SvgSquarer;

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

    public string ToAttributeValue()
    {
        return string.Join(' ', Format(MinX), Format(MinY), Format(Width), Format(Height));
    }

    // Emits whole numbers as integers to keep the output clean.
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
