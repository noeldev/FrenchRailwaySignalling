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
        var options = CommandLineOptions.Parse(args);

        if (options.ShowHelp)
        {
            PrintUsage();
            return ExitSuccess;
        }

        if (!Directory.Exists(options.Root))
        {
            Console.Error.WriteLine($"error: directory not found: {Path.GetFullPath(options.Root)}");
            return ExitUsageError;
        }

        if (!options.DryRun && options.OutputPath != null)
        {
            try
            {
                Directory.CreateDirectory(options.OutputPath);
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

        foreach (var path in Directory.EnumerateFiles(options.Root, SvgPattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(options.Root, path);
            var targetPath = options.OutputPath != null ? Path.Combine(options.OutputPath, relative) : path;

            var result = SvgFileProcessor.Process(path, targetPath, options.DryRun);

            switch (result.Status)
            {
                case ProcessStatus.Squared:
                    squared++;
                    var roundedNote = result.Rounded ? " [rounded, off-center by <= 0.5]" : string.Empty;
                    Console.WriteLine($"[squared] {relative}: {result.Detail}{roundedNote}");
                    break;

                case ProcessStatus.AlreadySquare:
                    alreadySquare++;
                    if (options.Verbose)
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

        var mode = options.DryRun ? " (dry-run, no files written)" : string.Empty;

        Console.WriteLine();
        Console.WriteLine($"Done{mode}. Squared: {squared}, already square: {alreadySquare}, warnings: {warnings}.");

        return warnings > 0 ? ExitWarnings : ExitSuccess;
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
