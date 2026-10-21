// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace MapRouletteGen.Core;

// Parses and holds the command line configuration. Parse returns null when the
// arguments are invalid or when help or the query was requested, so Program can
// exit early. Relative defaults from the settings are resolved against the
// repository root.
public sealed class CommandLineOptions
{
    public required string InputPath { get; init; }

    public required string OutputPath { get; init; }

    public required string RulesPath { get; init; }

    // True when the wiki pages are read from their local copy (the reviewed
    // backup) instead of being downloaded.
    public bool UseLocalPages { get; init; }

    // Challenge texts and statistics, next to the challenge file.
    public string TextPath => Path.ChangeExtension(OutputPath, ".md");

    // One sample node per correction, with its original tags, to test the
    // JOSM validator fixes.
    public required string SamplesPath { get; init; }

    public static CommandLineOptions? Parse(string[] args, GeneratorSettings settings, string repositoryRoot)
    {
        string? inputPath = null;
        string? outputPath = null;
        string? rulesPath = null;
        string? samplesPath = null;
        var useLocalPages = false;

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

                case "--print-query":
                    Console.WriteLine(string.Join(Environment.NewLine, settings.OverpassQuery));
                    return null;

                case "--out":
                    {
                        if (!TryReadValue(args, ref i, out var value)) return null;
                        outputPath = value;
                        break;
                    }

                case "--samples":
                    {
                        if (!TryReadValue(args, ref i, out var value)) return null;
                        samplesPath = value;
                        break;
                    }

                case "--rules":
                    {
                        if (!TryReadValue(args, ref i, out var value)) return null;
                        rulesPath = value;
                        break;
                    }

                default:
                    if (arg.StartsWith('-') || inputPath is not null)
                    {
                        Console.Error.WriteLine($"Unknown argument: {arg}");
                        PrintUsage();
                        return null;
                    }

                    inputPath = arg;
                    break;
            }
        }

        if (inputPath is null)
        {
            Console.Error.WriteLine("Missing input: the Overpass export (see --print-query).");
            PrintUsage();
            return null;
        }

        return new CommandLineOptions
        {
            InputPath = inputPath,
            OutputPath = outputPath ?? Path.Combine(repositoryRoot, settings.Output),
            RulesPath = rulesPath ?? Path.Combine(repositoryRoot, settings.Rules),
            SamplesPath = samplesPath ?? Path.Combine(repositoryRoot, settings.Samples),
            UseLocalPages = useLocalPages,
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
            Generates a MapRoulette tag fix challenge (line-by-line GeoJSON) that migrates
            legacy French railway signal tagging, with the fixes of the validator rule table
            and the corrections of mistyped values against the values the wiki documents.

            Usage:
              MapRouletteGen <export> [options]

            Arguments:
              <export>                Overpass export of the signals to check, raw data
                                      (JSON) or GeoJSON, for example from overpass-turbo
                                      with the query printed by --print-query.

            Options:
              --out <path>            Challenge file (default: the configured output,
                                      relative to the repository root). The challenge
                                      texts and statistics are written next to it (.md).
              --samples <path>        One sample node per correction, with its original
                                      tags, to test the JOSM validator fixes (default:
                                      the configured file, in the data folder).
              --rules <path>          Rule table (default: the configured file).
              --local                 Read the wiki pages from their local copy (the
                                      reviewed backup) instead of the live wiki.
              --print-query           Print the Overpass query and exit.
              -h, --help              Show this help.
            """);
    }
}
