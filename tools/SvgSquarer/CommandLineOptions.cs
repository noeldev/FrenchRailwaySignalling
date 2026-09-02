// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SvgSquarer;

// Parsed command-line arguments. The single positional value is the root folder;
// the rest are flags, except --backup, --restore and --size which consume the
// following token.
internal sealed record CommandLineOptions(
    string Root,
    string? BackupPath,
    string? RestorePath,
    bool Force,
    int? Size,
    bool DryRun,
    bool Verbose,
    bool ShowHelp)
{
    // True when --restore was given: the run reverses squaring instead of
    // applying it.
    public bool IsRestore => RestorePath != null;

    public static CommandLineOptions Parse(string[] args)
    {
        string? root = null;
        string? backupPath = null;
        string? restorePath = null;
        var force = false;
        int? size = null;
        var dryRun = false;
        var verbose = false;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "--force":
                    force = true;
                    break;

                case "--backup" or "-b":
                    if (i + 1 < args.Length)
                    {
                        backupPath ??= args[++i];
                    }
                    break;

                case "--restore":
                    if (i + 1 < args.Length)
                    {
                        restorePath ??= args[++i];
                    }
                    break;

                case "--size" or "-s":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedSize) && parsedSize > 0)
                    {
                        size ??= parsedSize;
                        i++;
                    }
                    break;

                default:
                    // First positional argument is the root directory; unknown
                    // flags are ignored.
                    if (!args[i].StartsWith('-'))
                    {
                        root ??= args[i];
                    }
                    break;
            }
        }

        return new CommandLineOptions(
            root ?? Directory.GetCurrentDirectory(),
            backupPath,
            restorePath,
            force,
            size,
            dryRun,
            verbose,
            showHelp);
    }

    // Validates the flag combination. Returns null when valid, an error
    // message otherwise. Does not touch the filesystem.
    public string? Validate()
    {
        if (IsRestore)
        {
            if (BackupPath != null)
            {
                return "--backup cannot be combined with --restore.";
            }

            if (Force)
            {
                return "--force has no effect with --restore.";
            }

            return null;
        }

        if (BackupPath == null && !Force)
        {
            return "in-place modification requires either --backup <path> or --force.";
        }

        return null;
    }
}
