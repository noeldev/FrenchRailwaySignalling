// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace TagInfoGen;

// Turns relative preset icon paths into absolute raw URLs.
// Preset list entries sometimes omit the file extension (for example
// "icons/signals/C-C"), while the stored assets are SVG files, so a missing
// extension defaults to ".svg".
internal static class IconResolver
{
    private const string DefaultExtension = ".svg";

    public static string? Resolve(string baseUrl, string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        var normalized = iconPath.Replace('\\', '/').TrimStart('/');

        if (!Path.HasExtension(normalized))
        {
            normalized += DefaultExtension;
        }

        return $"{baseUrl.TrimEnd('/')}/{normalized}";
    }
}
