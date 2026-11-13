// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

public enum BotCommand
{
    Plan,
    Apply,
    Revert,
    Seed,
    Login,
    Rules,
}

// Parses and holds the command line of one command. Parse returns null when
// the arguments are invalid or when help was requested, so Program can exit
// early. The interactive session builds the same options from its questions.
public sealed record CommandLineOptions
{
    public required BotCommand Command { get; init; }

    // Server profile of the settings; the configured default when null.
    public string? Server { get; init; }

    // Code of the country profile (countries/<code>.json); the only one
    // when null and there is a single profile.
    public string? Country { get; init; }

    // Filter: codes of the areas to process (regions, departments...); the
    // whole country when empty.
    public IReadOnlyList<string> Areas { get; init; } = [];

    // Filter: only the nodes whose last version is by this mapper.
    public string? Mapper { get; init; }

    // Overpass export read instead of querying Overpass.
    public string? InputPath { get; init; }

    // File of node ids (one per line) read instead of querying Overpass.
    public string? NodesPath { get; init; }

    // Journal of the run to revert; the last apply run of the server when null.
    public string? JournalPath { get; init; }

    // Largest number of edits (plan, apply, revert) or of test nodes (seed).
    public int? Limit { get; init; }

    // Plan from the input as it is, without reading the server.
    public bool Offline { get; init; }

    // Skip the confirmation prompt of a writing command.
    public bool AssumeYes { get; init; }

    public bool Writes => Command is BotCommand.Apply or BotCommand.Revert or BotCommand.Seed;

    public static CommandLineOptions? Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return null;
        }

        if (!Enum.TryParse<BotCommand>(args[0], ignoreCase: true, out var command) || int.TryParse(args[0], out _))
        {
            return Fail($"Unknown command: {args[0]}");
        }

        string? server = null;
        string? country = null;
        string? mapper = null;
        string? inputPath = null;
        string? nodesPath = null;
        string? journalPath = null;
        int? limit = null;
        List<string> areas = [];
        var offline = false;
        var assumeYes = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage();
                    return null;

                case "--offline":
                    offline = true;
                    break;

                case "--yes":
                    assumeYes = true;
                    break;

                case "--server":
                    if (!TryReadValue(args, ref i, out server)) return null;
                    break;

                case "--country":
                    if (!TryReadValue(args, ref i, out country)) return null;
                    break;

                case "--mapper":
                    if (!TryReadValue(args, ref i, out mapper)) return null;
                    break;

                case "--input":
                    if (!TryReadValue(args, ref i, out inputPath)) return null;
                    break;

                case "--nodes":
                    if (!TryReadValue(args, ref i, out nodesPath)) return null;
                    break;

                case "--area":
                    {
                        if (!TryReadValue(args, ref i, out var value)) return null;
                        areas.AddRange(value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                        break;
                    }

                case "--limit":
                    {
                        if (!TryReadValue(args, ref i, out var value)) return null;
                        if (!int.TryParse(value, out var number) || number <= 0)
                        {
                            return Fail($"Invalid --limit: {value}");
                        }

                        limit = number;
                        break;
                    }

                default:
                    if (arg.StartsWith('-') || command != BotCommand.Revert || journalPath is not null)
                    {
                        return Fail($"Unknown argument: {arg}");
                    }

                    journalPath = arg;
                    break;
            }
        }

        // --area and --mapper filter the same Overpass query: they combine.
        var sources = (areas.Count > 0 || mapper is not null ? 1 : 0) + (inputPath is null ? 0 : 1) + (nodesPath is null ? 0 : 1);
        var error = command switch
        {
            _ when sources > 1 => "--input and --nodes do not combine with each other or with --area and --mapper.",
            BotCommand.Revert or BotCommand.Login or BotCommand.Rules when sources > 0 => $"--area, --mapper, --input and --nodes do not apply to {args[0]}.",
            BotCommand.Login or BotCommand.Rules when limit is not null => $"--limit does not apply to {args[0]}.",
            BotCommand.Seed when nodesPath is not null => "--nodes does not apply to seed.",
            not BotCommand.Plan when offline => "--offline only applies to plan.",
            BotCommand.Plan when offline && inputPath is null => "--offline needs --input.",
            _ => null,
        };

        if (error is not null)
        {
            return Fail(error);
        }

        return new CommandLineOptions
        {
            Command = command,
            Server = server,
            Country = country,
            Areas = areas,
            Mapper = mapper,
            InputPath = inputPath,
            NodesPath = nodesPath,
            JournalPath = journalPath,
            Limit = limit,
            Offline = offline,
            AssumeYes = assumeYes,
        };
    }

    private static CommandLineOptions? Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return null;
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
            Applies the fixes of the rule table of a country to the railway signals already
            in OpenStreetMap, as a documented automated edit. Nothing is written without the
            apply, revert or seed command, and the sandbox server is the default.

            Without a command, the program asks for each step.

            Usage:
              SignalBot                             Interactive session.
              SignalBot plan   [source] [options]   Dry run: report what apply would do, nothing
                                                    written.
              SignalBot apply  [source] [options]   Upload the fixes.
              SignalBot revert [journal] [options]  Restore the tags a run replaced, on the nodes
                                                    nobody edited since (default: the last apply
                                                    run of the server).
              SignalBot seed   [source] [options]   Sandbox only: create test nodes copied from
                                                    signals to fix (default: 50).
              SignalBot login  [options]            Register and authorize the bot account (OAuth 2).
              SignalBot rules  [options]            Write the list of the corrections.

            Source (default: the whole country, read from Overpass; on a sandbox, the test
            nodes of the last seed):
              --area <code>[,<code>]  Filter: only these areas, for example a region (BRE) or
                                      a department (14), as configured for the country.
              --mapper <name>         Filter: only the nodes last edited by this mapper.
              --input <path>          Overpass answer (raw data, JSON), for example one saved
                                      by an earlier run.
              --nodes <path>          Node ids, one per line, read from the server.

            Options:
              --country <code>        Country profile (countries/<code>.json); needed only
                                      when there are several.
              --server <name>         Server profile of the settings (default: the sandbox).
              --limit <n>             Largest number of edits of the run, or of test nodes.
              --offline               plan only, with --input: do not read the server.
              --yes                   Do not ask for confirmation before writing.
              -h, --help              Show this help.

            A run can be stopped with Ctrl+C: the current changeset is completed and
            closed first. Running the same command again resumes the work, since every
            decision is taken on fresh data.
            """);
    }
}
