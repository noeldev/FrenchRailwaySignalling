// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Osm;

namespace SignalBot.Core;

// The program started without a command. It asks, in this order:
// 1. the country (when there are several profiles);
// 2. the server (or to quit), and guides the first steps on it:
//    - not logged in: log in now?
//    - sandbox: nothing can be done there without logging in, nor without
//      test signals (the sandbox has none): create them now? A "no" leads
//      back to the choice of the server;
// 3. the signals to work on (see SignalSelector); a sandbox only has the
//    test nodes;
// 4. the step (see SessionSteps): only those that can be taken now, the first
//    one not done yet by default.
// The server and the signals chosen are shown above the steps and kept from
// one step to the next. Before a step runs, one sentence tells what it is
// about to do (see CommandRecap) and the operator is asked to proceed; a
// "no" leads back to the steps, where the signals or the server can be
// chosen again, or the program left.
// Every command runs exactly as from the command line.
public sealed class InteractiveSession(string repositoryRoot, StopSignal stopSignal)
{
    public async Task<int> RunAsync()
    {
        var country = ChooseCountry();
        while (true)
        {
            var server = await EnterServerAsync(country);
            if (server is null)
            {
                return 0;
            }

            var settings = SettingsLoader.Load(country);
            var state = new BotState(settings, server, repositoryRoot);

            var selection = state.Server.AllowSeeding ? SignalSelection.TestNodes : await SelectSignalsAsync(settings, state, current: null);
            if (selection is not null && await RunStepsAsync(country, server, selection))
            {
                return 0;
            }

            Console.WriteLine();
        }
    }

    // Offers the steps until the operator leaves: true to quit the program,
    // false to choose another server.
    private async Task<bool> RunStepsAsync(string country, string server, SignalSelection selection)
    {
        while (true)
        {
            // Read again: login writes the settings of the operator.
            var settings = SettingsLoader.Load(country);
            var state = new BotState(settings, server, repositoryRoot);
            var steps = SessionSteps.For(state);

            Console.WriteLine();
            Console.WriteLine($"{settings.Country.Name}, server '{server}', "
                + (state.IsLoggedIn ? $"bot account '{state.Server.Account}'." : "not logged in."));
            Console.WriteLine($"Signals: {selection.Describe(settings)}.");
            var next = steps.FirstOrDefault(step => !step.Done)?.Name ?? SessionSteps.Exit;
            var choice = ConsolePrompt.Choose("Step", [.. steps.Select(step => (step.Name, step.Description))], next);
            Console.WriteLine();

            switch (choice)
            {
                case SessionSteps.Exit:
                    return true;

                case SessionSteps.Server:
                    return false;

                case SessionSteps.Signals:
                    selection = await SelectSignalsAsync(settings, state, selection) ?? selection;
                    continue;
            }

            var options = AskDetails(new CommandLineOptions { Command = Enum.Parse<BotCommand>(choice, ignoreCase: true), Server = server, Country = country }, settings, selection);
            if (options.Command == BotCommand.Login || Proceed(CommandRecap.Describe(options, settings, state, selection)))
            {
                await RunCommandAsync(settings, options);
            }
        }
    }

    private static bool Proceed(string recap)
    {
        Console.WriteLine(recap);
        if (ConsolePrompt.YesNo("Do you want to proceed?", defaultValue: false))
        {
            Console.WriteLine();
            return true;
        }

        Console.WriteLine("Nothing was done.");
        return false;
    }

    // Asks for a server until the first steps on it are taken; null to quit.
    private async Task<string?> EnterServerAsync(string country)
    {
        while (true)
        {
            var server = ChooseServer(SettingsLoader.Load(country));
            if (server == SessionSteps.Exit)
            {
                return null;
            }

            Console.WriteLine();
            if (await GuideAsync(country, server))
            {
                return server;
            }

            Console.WriteLine();
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
        await RunCommandAsync(settings, AskDetails(seed, settings, SignalSelection.TestNodes));
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
            .Append((SessionSteps.Exit, "quit"))
            .ToList();
        return ConsolePrompt.Choose("Server", choices, settings.DefaultServer);
    }

    // The mappers typed are looked up on the server chosen.
    private static async Task<SignalSelection?> SelectSignalsAsync(BotSettings settings, BotState state, SignalSelection? current)
    {
        using var client = OsmHttpClient.Create(settings, state.Server);
        var editor = new OptedInMapperEditor(new OsmApiClient(client, settings.Limits), new OptedInMapperStore(UserProfile.OptedInMappersPath(settings.Country.Code)));
        return await new SignalSelector(settings, editor).AskAsync(current, CancellationToken.None);
    }

    // What a command still needs to know: the signals chosen, the number of
    // test signals of seed (which copies signals of the whole country), the
    // largest number of edits of apply.
    private static CommandLineOptions AskDetails(CommandLineOptions options, BotSettings settings, SignalSelection selection) =>
        options.Command switch
        {
            BotCommand.Seed => options with { Limit = ConsolePrompt.Number("Number of test signals", Execution.Seeder.DefaultCount) },
            BotCommand.Plan => selection.ApplyTo(options),
            BotCommand.Apply => selection.ApplyTo(options) with { Limit = ConsolePrompt.Number("Largest number of edits", settings.Limits.MaxEditsPerRun) },
            _ => options,
        };

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
