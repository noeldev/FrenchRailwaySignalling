// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Diagnostics;

namespace RailwayMapBot.Core;

// Opens an address in the default browser of the operator, after asking. The
// address is always printed too, in case no browser can be started.
public static class Browser
{
    public static void Open(string url, string purpose)
    {
        ConsolePrompt.WaitForEnter($"Press Enter to open {purpose} in your browser...");
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.WriteLine("  No browser could be started.");
        }

        Console.WriteLine($"  If the page did not open, copy this address into the browser:");
        Console.WriteLine($"  {url}");
        Console.WriteLine();
    }
}
