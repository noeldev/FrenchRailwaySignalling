// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Reflection;

namespace RailwayMapBot.Core;

// Prints the startup header. Product, version and copyright come from the
// assembly metadata so they stay defined in a single place (the project file).
public static class Banner
{
    private const string DefaultName = "RailwayMapBot";

    public static void Print()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var name = assembly.GetName();

        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? name.Name ?? DefaultName;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        Console.WriteLine($"{product} {name.Version?.ToString(3) ?? "1.0.0"}");
        if (!string.IsNullOrEmpty(copyright))
        {
            Console.WriteLine(copyright);
        }

        Console.WriteLine();
    }

    // Product name and version, for the User-Agent header and the created_by
    // tag of the changesets.
    public static string ProductVersion() =>
        $"{ProductName()}/{typeof(Banner).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";

    public static string ProductName() => typeof(Banner).Assembly.GetName().Name ?? DefaultName;
}
