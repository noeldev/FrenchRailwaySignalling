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

        var validationError = options.Validate();
        if (validationError != null)
        {
            Console.Error.WriteLine($"error: {validationError}");
            return ExitUsageError;
        }

        if (!Directory.Exists(options.Root))
        {
            Console.Error.WriteLine($"error: directory not found: {Path.GetFullPath(options.Root)}");
            return ExitUsageError;
        }

        return options.IsRestore ? RunRestore(options) : RunSquare(options);
    }

    private static int RunSquare(CommandLineOptions options)
    {
        var backupStore = options.BackupPath != null ? new BackupStore(options.BackupPath) : null;

        var squared = 0;
        var alreadySquare = 0;
        var warnings = 0;

        foreach (var path in Directory.EnumerateFiles(options.Root, SvgPattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(options.Root, path);
            var result = SvgFileProcessor.Process(path, relative, options.Size, backupStore, options.DryRun);

            switch (result.Status)
            {
                case ProcessStatus.Squared:
                    squared++;
                    var roundedNote = result.Rounded ? " [rounded, off-center by <= 0.5]" : string.Empty;
                    Console.WriteLine($"[squared] {relative}: {result.Detail}{roundedNote}");
                    break;

                case ProcessStatus.AlreadySquare:
                    alreadySquare++;
                    if (options.Verbose || result.Written)
                    {
                        var label = result.Written ? "[resized] " : "[skip]    ";
                        Console.WriteLine($"{label}{relative}: {result.Detail}");
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

    private static int RunRestore(CommandLineOptions options)
    {
        var restored = 0;
        var copied = 0;
        var warnings = 0;

        foreach (var path in Directory.EnumerateFiles(options.Root, SvgPattern, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(options.Root, path);
            var targetPath = Path.Combine(options.RestorePath!, relative);
            var result = SvgRestoreProcessor.Restore(path, targetPath, options.Size, options.DryRun);

            switch (result.Status)
            {
                case RestoreStatus.Restored:
                    restored++;
                    Console.WriteLine($"[restored] {relative}: {result.Detail}");
                    break;

                case RestoreStatus.AlreadyOriginal:
                case RestoreStatus.NotSquared:
                    copied++;
                    if (options.Verbose)
                    {
                        Console.WriteLine($"[copy]     {relative}: {result.Detail}");
                    }
                    break;

                case RestoreStatus.Unrecognized:
                    warnings++;
                    Console.Error.WriteLine($"[warn]     {relative}: {result.Detail}");
                    break;
            }
        }

        var mode = options.DryRun ? " (dry-run, no files written)" : string.Empty;

        Console.WriteLine();
        Console.WriteLine($"Done{mode}. Restored: {restored}, copied as-is: {copied}, warnings: {warnings}.");

        return warnings > 0 ? ExitWarnings : ExitSuccess;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: SvgSquarer <directory> [--backup <path>] [--force] [--size <n>] [--dry-run] [--verbose]
                   SvgSquarer <directory> --restore <path> [--size <n>] [--dry-run] [--verbose]

              directory         Root folder to scan recursively.
              --backup, -b      Back up each file before it is modified in place (hash-deduplicated).
              --force           Allow in-place modification with no backup. Required when --backup is omitted.
              --size, -s        Add width/height (single value, since the result is always square) to
                                svg elements that declare neither. Existing width/height are left untouched.
              --restore <path>  Reverse the squaring: write restored copies under <path>, source untouched.
                                With --size <n>, also removes width/height when they equal n.
              --dry-run         Report changes without writing any file.
              --verbose         Also list files that needed no change.
              --help, -h        Show this help.

              Squares the root viewBox of every SVG by expanding the smaller axis to
              match the larger one and centering the original content. All other
              markup is left untouched.

              In-place modification requires --backup <path> or an explicit --force.
            """);
    }
}
