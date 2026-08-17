// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Reflection;

namespace PresetValidator.Core;

// Prints the startup header. Product, version and copyright come from the
// assembly metadata so they stay defined in a single place (the project file).
public static class Banner
{
    public static void Print()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var name = assembly.GetName();

        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? name.Name
            ?? "PresetValidator";
        var version = name.Version?.ToString(3) ?? "1.0.0";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        Console.WriteLine($"{product} {version}");
        if (!string.IsNullOrEmpty(copyright))
        {
            Console.WriteLine(copyright);
        }

        Console.WriteLine();
    }
}
