// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace ValidatorGen.Core;

// Parses and holds the command line configuration. Parse returns null when the
// arguments are invalid or when help was requested, so Program can exit early
// after printing usage. Relative defaults from the settings are resolved
// against the repository root.
public sealed class CommandLineOptions
{
    private const int DefaultTimeoutSeconds = 30;

    public required string OutputPath { get; init; }

    public required string RulesPath { get; init; }

    // True when every page is read from its local copy instead of being
    // downloaded.
    public bool UseLocalPages { get; init; }

    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    public static CommandLineOptions? Parse(string[] args, ValidatorSettings settings, string repositoryRoot)
    {
        string? outputPath = null;
        string? rulesPath = null;
        var useLocalPages = false;
        var timeout = DefaultTimeoutSeconds;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage();
                    return null;

                case "--local":
                    useLocalPages = true;
                    break;

                case "--rules":
                    {
                        if (!TryReadValue(args, ref i, out var value)) return null;
                        rulesPath = value;
                        break;
                    }

                case "--timeout":
                    {
                        if (!TryReadValue(args, ref i, out var value)) return null;
                        if (!int.TryParse(value, out timeout) || timeout <= 0)
                        {
                            Console.Error.WriteLine($"Invalid value for --timeout: {value}");
                            return null;
                        }

                        break;
                    }

                default:
                    if (arg.StartsWith('-') || outputPath is not null)
                    {
                        Console.Error.WriteLine($"Unknown argument: {arg}");
                        PrintUsage();
                        return null;
                    }

                    outputPath = arg;
                    break;
            }
        }

        return new CommandLineOptions
        {
            OutputPath = outputPath ?? Path.Combine(repositoryRoot, settings.Output),
            RulesPath = rulesPath ?? Path.Combine(repositoryRoot, settings.Rules),
            UseLocalPages = useLocalPages,
            TimeoutSeconds = timeout,
        };
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Missing value after {args[index]}");
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            Generates the MapCSS validator rules (JOSM tag checker, Osmose-QA) for French
            railway signalling from the OSM wiki documentation and the rule table.

            Usage:
              ValidatorGen [output.mapcss] [options]

            Arguments:
              [output.mapcss]         Output file (default: the configured output, relative
                                      to the repository root).

            Options:
              --local                 Read every page from its configured local copy (the
                                      reviewed backup) instead of the live wiki.
              --rules <path>          Rule table: replacements and conflicts (default: the
                                      configured file).
              --timeout <sec>         HTTP timeout (default: 30).
              -h, --help              Show this help.
            """);
    }
}
