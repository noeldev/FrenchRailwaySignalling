// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Osm;

namespace SignalBot.Core;

// The "mapper" choice of the interactive session: lists the mappers who opted
// in, adds (opt-in) or removes (opt-out) mappers one display name at a time,
// then asks whether to fix their signals. Every display name typed is checked
// on the server, which gives its uid.
public sealed class OptedInMapperEditor(OsmApiClient api, OptedInMapperStore store)
{
    private const string OptIn = "opt-in";
    private const string OptOut = "opt-out";

    // True when the operator confirmed fixing the signals of the opted-in
    // mappers; false to go back to the choice of the signals.
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var mappers = store.Load();
        Print(mappers);

        Console.WriteLine("Mappers:");
        var choice = ConsolePrompt.Choose("Mappers",
        [
            (OptIn, "add mappers whose signals may be fixed"),
            (OptOut, "remove mappers from the list"),
        ], OptIn);

        Console.WriteLine("One display name per line, as on the profile page; an empty line ends.");
        while (true)
        {
            var name = ConsolePrompt.Ask(choice == OptIn ? "Add mapper" : "Remove mapper");
            if (name.Length == 0)
            {
                break;
            }

            try
            {
                Console.WriteLine(choice == OptIn ? await AddAsync(mappers, name, cancellationToken) : await RemoveAsync(mappers, name, cancellationToken));
                store.Save(mappers);
            }
            catch (Exception ex) when (ex is HttpRequestException or OsmApiException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"  The server could not be asked about '{name}': {ex.Message}");
            }
        }

        Console.WriteLine();
        Print(mappers);
        return mappers.Users.Count > 0
            && ConsolePrompt.YesNo($"Fix only the signals last edited by {(mappers.Users.Count == 1 ? "this mapper" : $"these {mappers.Users.Count} mappers")}?", defaultValue: true);
    }

    private async Task<string> AddAsync(OptedInMappers mappers, string name, CancellationToken cancellationToken)
    {
        var user = await api.FindUserAsync(name, cancellationToken);
        if (user is null)
        {
            return $"  No mapper named '{name}' (no changeset under this name).";
        }

        return mappers.Add(user) ? $"  Added: {user}" : $"  Already in the list: {user}";
    }

    // By the name in the list first; a mapper who changed their name since
    // is found by uid.
    private async Task<string> RemoveAsync(OptedInMappers mappers, string name, CancellationToken cancellationToken)
    {
        var user = mappers.Remove(name);
        if (user is null && await api.FindUserAsync(name, cancellationToken) is { } found)
        {
            user = mappers.Remove(found.Uid);
        }

        return user is null ? $"  Not in the list: {name}" : $"  Removed: {user}";
    }

    private static void Print(OptedInMappers mappers)
    {
        if (mappers.Users.Count == 0)
        {
            Console.WriteLine("No mapper has opted in.");
            return;
        }

        Console.WriteLine("Opted-in mappers:");
        foreach (var user in mappers.Users)
        {
            Console.WriteLine($"  {user}");
        }
    }
}
