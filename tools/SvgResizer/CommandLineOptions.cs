// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SvgResizer;

// Parsed command-line arguments. The single positional value is a root file
// or folder; the rest are flags, except --backup, --width and --height which
// consume the following token.
internal sealed record CommandLineOptions(
    string Root,
    int? Width,
    int? Height,
    bool KeepAspectRatio,
    string? BackupPath,
    bool Force,
    bool DryRun,
    bool Verbose,
    bool ShowHelp)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? root = null;
        int? width = null;
        int? height = null;
        var keepAspectRatio = false;
        string? backupPath = null;
        var force = false;
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

                case "--keep-aspect-ratio" or "-k":
                    keepAspectRatio = true;
                    break;

                case "--backup" or "-b":
                    if (i + 1 < args.Length)
                    {
                        backupPath ??= args[++i];
                    }
                    break;

                case "--width" or "-w":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedWidth) && parsedWidth > 0)
                    {
                        width ??= parsedWidth;
                        i++;
                    }
                    break;

                case "--height":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedHeight) && parsedHeight > 0)
                    {
                        height ??= parsedHeight;
                        i++;
                    }
                    break;

                default:
                    // First positional argument is the root file or folder;
                    // unknown flags are ignored.
                    if (!args[i].StartsWith('-'))
                    {
                        root ??= args[i];
                    }
                    break;
            }
        }

        return new CommandLineOptions(
            root ?? Directory.GetCurrentDirectory(),
            width,
            height,
            keepAspectRatio,
            backupPath,
            force,
            dryRun,
            verbose,
            showHelp);
    }

    // Validates the flag combination. Returns null when valid, an error
    // message otherwise. Does not touch the filesystem.
    public string? Validate()
    {
        if (Width is null && Height is null)
        {
            return "--width <n> or --height <n> is required.";
        }

        if (BackupPath == null && !Force)
        {
            return "in-place modification requires either --backup <path> or --force.";
        }

        return null;
    }
}
