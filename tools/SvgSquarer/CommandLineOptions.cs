// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SvgSquarer;

// Parsed command-line arguments. The single positional value is a root file
// or folder; the rest are flags.
internal sealed record CommandLineOptions(
    string Root,
    bool Unsquare,
    bool DryRun,
    bool Verbose,
    bool ShowHelp)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? root = null;
        var unsquare = false;
        var dryRun = false;
        var verbose = false;
        var showHelp = false;

        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--help" or "-h":
                    showHelp = true;
                    break;

                case "--unsquare":
                    unsquare = true;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                default:
                    // First positional argument is the root file or folder;
                    // unknown flags are ignored.
                    if (!arg.StartsWith('-'))
                    {
                        root ??= arg;
                    }
                    break;
            }
        }

        return new CommandLineOptions(
            root ?? Directory.GetCurrentDirectory(),
            unsquare,
            dryRun,
            verbose,
            showHelp);
    }
}
