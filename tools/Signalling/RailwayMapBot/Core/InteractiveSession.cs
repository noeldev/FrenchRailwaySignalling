// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Core;

// The program started without a command: it asks for the server, offers to
// log in, then asks for one command after the other, with the few values
// each one needs. Every command runs exactly as from the command line.
public sealed class InteractiveSession(string? settingsPath, string repositoryRoot, StopSignal stopSignal)
{
    private const string ServerChoice = "server";
    private const string ExitChoice = "exit";

    public async Task<int> RunAsync()
    {
        PrintIntroduction();
        var settings = SettingsLoader.Load(settingsPath);
        var server = ChooseServer(settings);
        var loginOffered = false;

        while (true)
        {
            // Read again: login and approve write the settings of the operator.
            settings = SettingsLoader.Load(settingsPath);
            var loggedIn = BotApplication.IsLoggedIn(settings, server);
            if (!loggedIn && !loginOffered)
            {
                loginOffered = true;
                Console.WriteLine($"Not logged in to '{server}': seed, apply and revert need the bot account, plan does not.");
                if (ConsolePrompt.YesNo("Log in now?", defaultValue: true))
                {
                    await RunCommandAsync(settings, new CommandLineOptions { Command = BotCommand.Login, Server = server, SettingsPath = settingsPath });
                    continue;
                }
            }

            Console.WriteLine();
            Console.WriteLine(loggedIn
                ? $"Server '{server}', bot account '{settings.Servers[server].Account}'."
                : $"Server '{server}', not logged in.");

            var choice = ConsolePrompt.Choose("Command", Commands(settings.Servers[server]), BotCommand.Plan.ToString().ToLowerInvariant());
            Console.WriteLine();
            if (choice == ExitChoice)
            {
                return 0;
            }

            if (choice == ServerChoice)
            {
                server = ChooseServer(settings);
                loginOffered = false;
                continue;
            }

            var command = Enum.Parse<BotCommand>(choice, ignoreCase: true);
            await RunCommandAsync(settings, AskOptions(command, settings, server));
        }
    }

    private static void PrintIntroduction()
    {
        Console.WriteLine(
            """
            The bot applies the fixes of the rule table to the signals already in OpenStreetMap.
            Usual order:
              1. On prod, plan: dry run on the real data, nothing written. Read the report.
              2. On dev (the sandbox, separate from the real map): seed creates test nodes,
                 then plan, apply and revert work on them.
              3. rules writes the list of the corrections for the wiki page; once the edit
                 is discussed and accepted, approve on prod.
              4. On prod, apply, a small area first, then wait for feedback.

            """);
    }

    private static string ChooseServer(BotSettings settings)
    {
        Console.WriteLine("Server:");
        var choices = settings.Servers
            .Select(server => (server.Key, server.Value.AllowSeeding
                ? $"sandbox, separate from the real map ({server.Value.WebUrl})"
                : $"the real map ({server.Value.WebUrl})"))
            .ToList();
        return ConsolePrompt.Choose("Server", choices, settings.DefaultServer);
    }

    private static List<(string Name, string Description)> Commands(ServerSettings server)
    {
        List<(string Name, string Description)> commands =
        [
            (Name(BotCommand.Plan), server.AllowSeeding
                ? "dry run on the test nodes: what apply would change, nothing written"
                : "dry run on the real data: what apply would change, nothing written"),
        ];

        if (server.AllowSeeding)
        {
            commands.Add((Name(BotCommand.Seed), "create test nodes, copied from real signals to fix"));
        }

        commands.Add((Name(BotCommand.Apply), server.AllowSeeding ? "fix the test nodes" : "upload the fixes to the real map"));
        commands.Add((Name(BotCommand.Revert), "undo the last apply run"));
        commands.Add((Name(BotCommand.Rules), "write the list of the corrections (to read, and for the wiki)"));
        if (!server.AllowSeeding)
        {
            commands.Add((Name(BotCommand.Approve), "record the rule table as discussed and accepted"));
        }

        commands.Add((Name(BotCommand.Login), "log in with the bot account"));
        commands.Add((ServerChoice, "change server"));
        commands.Add((ExitChoice, "quit"));
        return commands;
    }

    private CommandLineOptions AskOptions(BotCommand command, BotSettings settings, string server)
    {
        var options = new CommandLineOptions { Command = command, Server = server, SettingsPath = settingsPath };
        var sandbox = settings.Servers[server].AllowSeeding;
        var codes = string.Join(", ", settings.Areas.Select(area => area.Code));

        return command switch
        {
            BotCommand.Seed => options with
            {
                Areas = [ConsolePrompt.Required($"Area to copy the signals from ({codes})")],
                Limit = ConsolePrompt.Number("Number of test nodes", Execution.Seeder.DefaultCount),
            },
            BotCommand.Plan when !sandbox => options with
            {
                Areas = AskAreas(codes),
            },
            BotCommand.Apply when !sandbox => options with
            {
                Areas = AskAreas(codes),
                Limit = ConsolePrompt.Number("Largest number of edits", settings.Limits.MaxEditsPerRun),
            },
            _ => options,
        };
    }

    private static List<string> AskAreas(string codes) =>
    [
        .. ConsolePrompt.Ask($"Areas, comma-separated, empty for all ({codes})")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
    ];

    // An error ends the command, not the session.
    private async Task RunCommandAsync(BotSettings settings, CommandLineOptions options)
    {
        try
        {
            await new BotApplication(settings, options, repositoryRoot).RunAsync(stopSignal.Start());
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Stopped by the operator.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            stopSignal.End();
        }
    }

    private static string Name(BotCommand command) => command.ToString().ToLowerInvariant();
}
