// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using System.Text;

namespace SvgSquarer;

// Shared read/write helpers that preserve the original file's UTF-8 byte
// order mark, used by both the squaring and restore processors.
internal static class Utf8TextFile
{
    private static readonly UTF8Encoding NoBom = new(false);

    public static string Read(byte[] bytes, out bool hasBom)
    {
        hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        var bomLength = hasBom ? Encoding.UTF8.Preamble.Length : 0;
        return NoBom.GetString(bytes, bomLength, bytes.Length - bomLength);
    }

    public static void Write(string path, string text, bool hasBom)
    {
        File.WriteAllText(path, text, new UTF8Encoding(hasBom));
    }
}
