// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SvgSquarer;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitWarnings = 1;
    private const int ExitUsageError = 2;

    private const string SvgPattern = "*.svg";

    private static int Main(string[] args)
    {
        if (HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintUsage();
            return ExitSuccess;
        }

        var dryRun = HasFlag(args, "--dry-run");
        var verbose = HasFlag(args, "--verbose");
        var root = GetRootDirectory(args);
        var outputPath = GetOutputDirectory(args);

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"error: directory not found: {Path.GetFullPath(root)}");
            return ExitUsageError;
        }

        if (!dryRun && outputPath != null)
        {
            try
            {
                Directory.CreateDirectory(outputPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: failed to create output directory: {ex.Message}");
                return ExitUsageError;
            }
        }

        var squared = 0;
        var alreadySquare = 0;
        var warnings = 0;

        foreach (var path in Directory.EnumerateFiles(root, SvgPattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            var targetPath = outputPath != null ? Path.Combine(outputPath, relative) : path;

            var result = SvgFileProcessor.Process(path, targetPath, dryRun);

            switch (result.Status)
            {
                case ProcessStatus.Squared:
                    squared++;
                    string roundedNote = result.Rounded ? " [rounded, off-center by <= 0.5]" : string.Empty;
                    Console.WriteLine($"[squared] {relative}: {result.Detail}{roundedNote}");
                    break;

                case ProcessStatus.AlreadySquare:
                    alreadySquare++;
                    if (verbose)
                    {
                        Console.WriteLine($"[skip]    {relative}: already square ({result.Detail})");
                    }
                    break;

                case ProcessStatus.NoViewBox:
                case ProcessStatus.InvalidViewBox:
                    warnings++;
                    Console.Error.WriteLine($"[warn]    {relative}: {result.Detail}");
                    break;
            }
        }

        var mode = dryRun ? " (dry-run, no files written)" : string.Empty;

        Console.WriteLine();
        Console.WriteLine($"Done{mode}. Squared: {squared}, already square: {alreadySquare}, warnings: {warnings}.");

        return warnings > 0 ? ExitWarnings : ExitSuccess;
    }

    private static string GetRootDirectory(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.Ordinal) ||
                string.Equals(args[i], "-o", StringComparison.Ordinal))
            {
                i++; // Skip output path argument
                continue;
            }

            if (!args[i].StartsWith('-'))
            {
                return args[i];
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? GetOutputDirectory(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--output", StringComparison.Ordinal) ||
                string.Equals(args[i], "-o", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return Array.Exists(args, arg => string.Equals(arg, flag, StringComparison.Ordinal));
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: SvgSquarer [directory] [--output <path>] [--dry-run] [--verbose]
              directory         Root folder to scan recursively (default: current directory).
              --output, -o      Alternative output folder where processed files will be saved.
              --dry-run         Report changes without writing any file.
              --verbose         Also list files that are already square.
              --help, -h        Show this help.

            Squares the root viewBox of every SVG by expanding the smaller axis to
            match the larger one and centering the original content. width/height
            attributes and all other markup are left untouched.
            """);
    }
}