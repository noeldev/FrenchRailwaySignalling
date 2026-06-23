// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SvgSquarer;

// Parsed command-line arguments. The single positional value is the root folder;
// the rest are flags, except --output which consumes the following token.
internal sealed record CommandLineOptions(
    string Root,
    string? OutputPath,
    bool DryRun,
    bool Verbose,
    bool ShowHelp)
{
    public static CommandLineOptions Parse(string[] args)
    {
        string? root = null;
        string? output = null;
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

                case "--output" or "-o":
                    if (i + 1 < args.Length)
                    {
                        output ??= args[++i];
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
            output,
            dryRun,
            verbose,
            showHelp);
    }
}
