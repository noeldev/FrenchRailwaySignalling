// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Reflection;
using SvgSquarer;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitWarnings = 1;
    private const int ExitUsageError = 2;

    private const string SvgPattern = "*.svg";
    private const string ToolDescription = "Prepares SVG icons for JOSM presets - turns every non-square icon into a square, centered one, and back.";

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintBanner();
            PrintUsage();
            return ExitUsageError;
        }

        var options = CommandLineOptions.Parse(args);

        if (options.ShowHelp)
        {
            PrintBanner();
            PrintUsage();
            return ExitSuccess;
        }

        var isFile = File.Exists(options.Root);
        if (!isFile && !Directory.Exists(options.Root))
        {
            Console.Error.WriteLine($"error: file or directory not found: {Path.GetFullPath(options.Root)}");
            return ExitUsageError;
        }

        string[] files = isFile
            ? [options.Root]
            : [.. Directory.EnumerateFiles(options.Root, SvgPattern, SearchOption.AllDirectories)];

        var squared = 0;
        var unsquared = 0;
        var cleaned = 0;
        var unchanged = 0;
        var warnings = 0;

        foreach (var path in files)
        {
            var relative = isFile ? Path.GetFileName(path) : Path.GetRelativePath(options.Root, path);
            var result = SvgFileProcessor.Process(path, options.Unsquare, options.DryRun);

            switch (result.Status)
            {
                case ProcessStatus.Squared:
                    squared++;
                    Console.WriteLine($"[squared] {relative}: {result.Detail}");
                    break;

                case ProcessStatus.Unsquared:
                    unsquared++;
                    Console.WriteLine($"[unsquared] {relative}: {result.Detail}");
                    break;

                case ProcessStatus.Cleaned:
                    cleaned++;
                    Console.WriteLine($"[cleaned] {relative}: {result.Detail}");
                    break;

                case ProcessStatus.Unchanged:
                    unchanged++;
                    if (options.Verbose)
                    {
                        Console.WriteLine($"[skip]    {relative}: {result.Detail}");
                    }
                    break;

                case ProcessStatus.Warning:
                    warnings++;
                    Console.Error.WriteLine($"[warn]    {relative}: {result.Detail}");
                    break;
            }
        }

        var mode = options.DryRun ? " (dry-run, no files written)" : string.Empty;

        Console.WriteLine();
        Console.WriteLine($"Done{mode}. Squared: {squared}, unsquared: {unsquared}, cleaned: {cleaned}, unchanged: {unchanged}, warnings: {warnings}.");

        return warnings > 0 ? ExitWarnings : ExitSuccess;
    }

    private static void PrintBanner()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"v{version.ToString(3)}" : "v0.0.0";

        Console.WriteLine($"SvgSquarer {versionText}");
        Console.WriteLine(ToolDescription);
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage:

              SvgSquarer <file-or-directory> [--unsquare] [--dry-run] [--verbose]

              file-or-directory     A single .svg file, or a folder to scan recursively.
              --unsquare            Reverse the operation: remove the josm-square group and
                                    restore the viewBox it replaced.
              --dry-run             Report changes without writing any file.
              --verbose             Also list files that needed no change.
              --help, -h            Show this help.

              JOSM aligns non-square icons to the left of their slot and ignores
              both the viewBox origin and preserveAspectRatio. Every SVG with a
              non-square viewBox is therefore rewritten in place as a square
              document: viewBox 0 0 side side, and the original content wrapped
              in <g id="josm-square" transform="translate(...)"> to center it.
              --unsquare removes that group and restores the original viewBox,
              assuming its shorter axis started at 0. A square icon is never
              wrapped twice. width, height and preserveAspectRatio are removed
              from the root svg tag of every SVG.

              Files are modified in place: commit or stash first, git is the backup.
              Warnings are written to stderr: add 2>&1 to a redirection to keep them.
            """);
    }
}
