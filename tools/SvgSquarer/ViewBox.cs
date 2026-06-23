// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;

namespace SvgSquarer;

// Parsed SVG viewBox (min-x, min-y, width, height) with squaring helpers.
internal readonly struct ViewBox(double minX, double minY, double width, double height)
{
    private const double Epsilon = 1e-9;
    private static readonly char[] Separators = { ' ', '\t', '\r', '\n', ',' };

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

    // Returns a square viewBox of side max(width, height) with the original
    // content centered at 0 0. "rounded" reports whether integer rounding 
    // shifted the content off perfect center (at most 0.5).
    public ViewBox ToSquaredCentered(out double tx, out double ty, out bool rounded)
    {
        var exactSide = Math.Max(Width, Height);
        var side = (int)Math.Ceiling(exactSide - Epsilon);

        var exactOffsetX = (side - Width) / 2.0;
        var exactOffsetY = (side - Height) / 2.0;

        var offsetX = (int)Math.Round(exactOffsetX, MidpointRounding.AwayFromZero);
        var offsetY = (int)Math.Round(exactOffsetY, MidpointRounding.AwayFromZero);

        // Calcul de la translation pour amener l'origine à 0 0 et centrer le contenu
        tx = offsetX - MinX;
        ty = offsetY - MinY;

        rounded =
            Math.Abs(side - exactSide) > Epsilon ||
            Math.Abs(exactOffsetX - offsetX) > Epsilon ||
            Math.Abs(exactOffsetY - offsetY) > Epsilon ||
            Math.Abs(MinX - Math.Round(MinX)) > Epsilon ||
            Math.Abs(MinY - Math.Round(MinY)) > Epsilon;

        return new ViewBox(0, 0, side, side);
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