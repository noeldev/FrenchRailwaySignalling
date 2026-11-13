// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Osm;

namespace SignalBot.Core;

// The program started without a command. It asks for the country (when there
// are several profiles) and the server, then guides the first steps on it:
// - not logged in: log in now?
// - sandbox: nothing can be done there without logging in, nor without test
//   signals (the sandbox has none): create them now? A "no" leads back to
//   the choice of the server.
// Then it offers only the steps that can be taken now, in their order, the
// first one not done yet by default:
// - real map: plan, login (until logged in), apply (once logged in), revert
//   (after an apply);
// - sandbox: login (until logged in), seed (once logged in), plan and apply
//   (once test nodes exist), revert (after an apply).
// Every command runs exactly as from the command line.
public sealed class InteractiveSession(string repositoryRoot, StopSignal stopSignal)
{
    private const string ServerChoice = "server";
    private const string ExitChoice = "exit";

    public async Task<int> RunAsync()
    {
        var country = ChooseCountry();
        var server = await EnterServerAsync(country);

        while (true)
        {
            // Read again: login writes the settings of the operator.
            var settings = SettingsLoader.Load(country);
            var state = new BotState(settings, server, repositoryRoot);
            var steps = Steps(state);

            Console.WriteLine();
            Console.WriteLine($"{settings.Country.Name}, server '{server}', "
                + (state.IsLoggedIn ? $"bot account '{state.Server.Account}'." : "not logged in."));
            var next = steps.FirstOrDefault(step => !step.Done).Name ?? ExitChoice;
            var choice = ConsolePrompt.Choose("Step", [.. steps.Select(step => (step.Name, step.Description))], next);
            Console.WriteLine();

            if (choice == ExitChoice)
            {
                return 0;
            }

            if (choice == ServerChoice)
            {
                server = await EnterServerAsync(country);
                continue;
            }

            var command = Enum.Parse<BotCommand>(choice, ignoreCase: true);
            var options = new CommandLineOptions { Command = command, Server = server, Country = country };
            await RunCommandAsync(settings, await AskSourceAsync(options, settings, state));
        }
    }

    // Asks for a server until the first steps on it are taken.
    private async Task<string> EnterServerAsync(string country)
    {
        while (true)
        {
            var server = ChooseServer(SettingsLoader.Load(country));
            if (await GuideAsync(country, server))
            {
                return server;
            }
        }
    }

    // False when nothing can be done on the server.
    private async Task<bool> GuideAsync(string country, string server)
    {
        var settings = SettingsLoader.Load(country);
        var state = new BotState(settings, server, repositoryRoot);
        if (!state.IsLoggedIn && ConsolePrompt.YesNo($"Not logged in to '{server}'. Log in now?", defaultValue: true))
        {
            await RunCommandAsync(settings, new CommandLineOptions { Command = BotCommand.Login, Server = server, Country = country });
            settings = SettingsLoader.Load(country);
            state = new BotState(settings, server, repositoryRoot);
        }

        if (!state.Server.AllowSeeding || state.HasSeededNodes)
        {
            return true;
        }

        if (!state.IsLoggedIn)
        {
            Console.WriteLine($"Nothing can be done on '{server}' without logging in.");
            return false;
        }

        if (!ConsolePrompt.YesNo($"The map of '{server}' has no signal to fix. Create test signals, copied from real ones?", defaultValue: true))
        {
            return false;
        }

        var seed = new CommandLineOptions { Command = BotCommand.Seed, Server = server, Country = country };
        await RunCommandAsync(settings, await AskSourceAsync(seed, settings, state));
        return true;
    }

    private static string ChooseCountry()
    {
        var codes = CountryProfiles.Codes();
        if (codes.Count <= 1)
        {
            return CountryProfiles.Resolve(null);
        }

        Console.WriteLine("Country:");
        return ConsolePrompt.Choose("Country", [.. codes.Select(code => (code, string.Empty))], codes[0]);
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

    // The steps that can be taken now, in their order. Done marks a step
    // already taken, so the default is the next one.
    private static List<(string Name, string Description, bool Done)> Steps(BotState state)
    {
        var sandbox = state.Server.AllowSeeding;
        var applyReady = state.IsLoggedIn && (!sandbox || state.HasSeededNodes);
        List<(string Name, string Description, bool Done)> steps = [];

        if (sandbox)
        {
            AddIf(steps, !state.IsLoggedIn, BotCommand.Login, "log in with the bot account of the sandbox", false);
            AddIf(steps, state.IsLoggedIn, BotCommand.Seed, "create test nodes, copied from real signals to fix", state.HasSeededNodes);
            AddIf(steps, state.HasSeededNodes, BotCommand.Plan, "dry run on the test nodes, nothing written", state.HasRun(BotCommand.Plan));
        }
        else
        {
            AddIf(steps, true, BotCommand.Plan, "dry run on the real data, nothing written", state.HasRun(BotCommand.Plan));
            AddIf(steps, !state.IsLoggedIn, BotCommand.Login, "log in with the bot account", false);
        }

        AddIf(steps, applyReady, BotCommand.Apply, sandbox ? "fix the test nodes" : "upload the fixes to the real map", state.LastApplyJournal is not null);
        AddIf(steps, state.LastApplyJournal is not null, BotCommand.Revert, "undo the last apply", true);
        steps.Add((ServerChoice, "change server", true));
        steps.Add((ExitChoice, "quit", true));
        return steps;
    }

    private static void AddIf(List<(string Name, string Description, bool Done)> steps, bool available, BotCommand command, string description, bool done)
    {
        if (available)
        {
            steps.Add((command.ToString().ToLowerInvariant(), description, done));
        }
    }

    // seed copies signals of the whole country: only their number is asked.
    // plan and apply on the real map: every signal of the country, those of
    // areas, or those of the mappers who opted in (see OptedInMapperEditor;
    // back to this choice when none is left or the operator declines). On a
    // sandbox, plan and apply work on the test nodes.
    private static async Task<CommandLineOptions> AskSourceAsync(CommandLineOptions options, BotSettings settings, BotState state)
    {
        if (options.Command is BotCommand.Seed)
        {
            return options with { Limit = ConsolePrompt.Number("Number of test signals", Execution.Seeder.DefaultCount) };
        }

        if (options.Command is not (BotCommand.Plan or BotCommand.Apply) || state.Server.AllowSeeding)
        {
            return options;
        }

        options = await AskSignalsAsync(options, settings, state);

        return options.Command is BotCommand.Apply
            ? options with { Limit = ConsolePrompt.Number("Largest number of edits", settings.Limits.MaxEditsPerRun) }
            : options;
    }

    private static async Task<CommandLineOptions> AskSignalsAsync(CommandLineOptions options, BotSettings settings, BotState state)
    {
        while (true)
        {
            Console.WriteLine("Signals:");
            var filter = ConsolePrompt.Choose("Signals",
            [
                ("all", $"every signal of {settings.Country.Name}"),
                ("area", "the signals of a region or a department"),
                ("mapper", "the signals last edited by the mappers who opted in"),
            ], "all");

            switch (filter)
            {
                case "area":
                    return options with { Areas = AskAreas(settings) };

                case "mapper":
                    {
                        using var client = OsmHttpClient.Create(settings, state.Server);
                        var editor = new OptedInMapperEditor(new OsmApiClient(client, settings.Limits), new OptedInMapperStore(UserProfile.OptedInMappersPath(settings.Country.Code)));
                        if (await editor.RunAsync(CancellationToken.None))
                        {
                            return options with { OptedIn = true };
                        }

                        Console.WriteLine();
                        break;
                    }

                default:
                    return options;
            }
        }
    }

    private static List<string> AskAreas(BotSettings settings)
    {
        Console.WriteLine(AreaResolver.Describe(settings.AreaKinds));
        return
        [
            .. ConsolePrompt.Required("Areas, comma-separated")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        ];
    }

    // An error ends the command, not the session.
    private async Task RunCommandAsync(BotSettings settings, CommandLineOptions options)
    {
        try
        {
            await new BotApplication(settings, options, repositoryRoot).RunAsync(stopSignal.Start(options.Writes));
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
}
