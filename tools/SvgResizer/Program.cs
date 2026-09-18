// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Reflection;
using SvgResizer;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitWarnings = 1;
    private const int ExitUsageError = 2;

    private const string SvgPattern = "*.svg";
    private const string ToolDescription = "Sets consistent SVG icon dimensions - square by default, or aspect-ratio-preserving with --keep-aspect-ratio.";

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

        var validationError = options.Validate();
        if (validationError != null)
        {
            Console.Error.WriteLine($"error: {validationError}");
            return ExitUsageError;
        }

        var isFile = File.Exists(options.Root);
        var isDirectory = !isFile && Directory.Exists(options.Root);

        if (!isFile && !isDirectory)
        {
            Console.Error.WriteLine($"error: file or directory not found: {Path.GetFullPath(options.Root)}");
            return ExitUsageError;
        }

        var backupStore = options.BackupPath != null ? new BackupStore(options.BackupPath) : null;

        var resized = 0;
        var alreadyCorrect = 0;
        var warnings = 0;

        var files = isFile
            ? new[] { options.Root }
            : Directory.EnumerateFiles(options.Root, SvgPattern, SearchOption.AllDirectories).ToArray();

        foreach (var path in files)
        {
            var relative = isFile ? Path.GetFileName(path) : Path.GetRelativePath(options.Root, path);
            var result = SvgFileProcessor.Process(
                path, relative, options.Width, options.Height, options.KeepAspectRatio, options.Force, backupStore, options.DryRun);

            switch (result.Status)
            {
                case ProcessStatus.Resized:
                    resized++;
                    Console.WriteLine($"[resized] {relative}: {result.Detail}");
                    break;

                case ProcessStatus.AlreadyCorrect:
                    alreadyCorrect++;
                    if (options.Verbose)
                    {
                        Console.WriteLine($"[skip]    {relative}: {result.Detail}");
                    }
                    break;

                case ProcessStatus.SizeMismatch:
                case ProcessStatus.NoViewBox:
                case ProcessStatus.InvalidViewBox:
                    warnings++;
                    Console.Error.WriteLine($"[warn]    {relative}: {result.Detail}");
                    break;
            }
        }

        var mode = options.DryRun ? " (dry-run, no files written)" : string.Empty;

        Console.WriteLine();
        Console.WriteLine($"Done{mode}. Resized: {resized}, already correct: {alreadyCorrect}, warnings: {warnings}.");

        return warnings > 0 ? ExitWarnings : ExitSuccess;
    }

    private static void PrintBanner()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionText = version != null ? $"v{version.ToString(3)}" : "v0.0.0";

        Console.WriteLine($"SvgResizer {versionText}");
        Console.WriteLine(ToolDescription);
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage:
            
              SvgResizer <file-or-directory> [--width <n>] [--height <n>] [--keep-aspect-ratio]
                         [--backup <path>] [--force] [--dry-run] [--verbose]

              file-or-directory     A single .svg file, or a folder to scan recursively.
              --width, -w <n>       Sets the width attribute. At least one of --width/--height
                                    is required.
              --height <n>          Sets the height attribute. At least one of --width/--height
                                    is required. No short form - -h is reserved for --help.
              --keep-aspect-ratio,  When only one of --width/--height is given, derives the
              -k                    other from the current viewBox's own aspect ratio instead
                                    of making the icon square. Ignored when both are given -
                                    with both set explicitly, there is nothing left to derive.
              --backup, -b          Back up each file before it is modified in place (hash-deduplicated).
              --force               Allow in-place modification with no backup, and allow
                                    overwriting width/height/preserveAspectRatio that already
                                    exist with a different value. Required when --backup is omitted.
              --dry-run             Report changes without writing any file.
              --verbose             Also list files that needed no change.
              --help, -h            Show this help.

              Sets width/height on every SVG without touching its viewBox or
              content, using preserveAspectRatio="xMidYMid meet" to keep the
              artwork centered and undistorted. Square by default (JOSM); pass
              --keep-aspect-ratio with a single dimension to instead preserve
              the icon's own proportions (e.g. for ORM rendering).

              In-place modification requires --backup <path> or an explicit --force.
            """);
    }
}
