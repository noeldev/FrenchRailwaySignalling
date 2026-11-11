// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Diagnostics;

namespace SignalBot.Core;

// Opens a page in the default browser of the operator, after asking. A short
// address is shown in the question, so it can be opened by hand (Ctrl+click
// in Windows Terminal); a long one only when no browser can be started.
public static class Browser
{
    private const int MaxShownLength = 80;

    public static void Open(string purpose, string url)
    {
        var shown = url.Length <= MaxShownLength;
        ConsolePrompt.WaitForEnter(shown ? $"Press Enter to open {purpose} ({url})." : $"Press Enter to open {purpose}.");
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.WriteLine($"No browser could be started: open {(shown ? "the address above" : url)} by hand.");
        }
    }
}
