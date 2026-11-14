// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Osm;

namespace SignalBot.Core;

// The "mapper" choice of the interactive session: a menu shown again after
// each change, with the list of the mappers who opted in, until the operator
// goes on or back:
// - opt-in adds mappers, one display name per line until an empty line;
//   every name typed is checked on the server, which gives its uid;
// - opt-out removes mappers, one per line until an empty line, by their
//   number in the list (shown again after each removal) or by display name;
// - continue goes on with the list as it is (offered once it is not empty);
// - back returns to the choice of the signals.
// Every change is saved at once: the list is the same in the next session.
public sealed class OptedInMapperEditor(OsmApiClient api, OptedInMapperStore store)
{
    private const string OptIn = "opt-in";
    private const string OptOut = "opt-out";
    private const string Continue = "continue";
    private const string Back = "back";

    // The mappers to go on with, or null when the operator went back.
    public async Task<OptedInMappers?> RunAsync(CancellationToken cancellationToken)
    {
        var mappers = store.Load();
        while (true)
        {
            Console.WriteLine();
            Print(mappers);

            var hasMappers = mappers.Users.Count > 0;
            List<(string Name, string Description)> choices = [(OptIn, "add mappers whose signals may be fixed")];
            if (hasMappers)
            {
                choices.Add((OptOut, "remove mappers from the list"));
                choices.Add((Continue, $"go on with {(mappers.Users.Count == 1 ? "this mapper" : $"these {mappers.Users.Count} mappers")}"));
            }

            choices.Add((Back, "back to the choice of the signals"));

            Console.WriteLine("Mappers:");
            switch (ConsolePrompt.Choose("Mappers", choices, hasMappers ? Continue : OptIn))
            {
                case Continue:
                    return mappers;

                case Back:
                    return null;

                case var choice:
                    await EditAsync(mappers, add: choice == OptIn, cancellationToken);
                    break;
            }
        }
    }

    private async Task EditAsync(OptedInMappers mappers, bool add, CancellationToken cancellationToken)
    {
        Console.WriteLine(add
            ? "One display name per line, as on the profile page; an empty line ends."
            : "One number of the list, or one display name, per line; an empty line ends.");
        while (add || mappers.Users.Count > 0)
        {
            var name = ConsolePrompt.Ask(add ? "Add mapper" : "Remove mapper");
            if (name.Length == 0)
            {
                return;
            }

            try
            {
                Console.WriteLine(add ? await AddAsync(mappers, name, cancellationToken) : await RemoveAsync(mappers, name, cancellationToken));
                store.Save(mappers);
                if (!add && mappers.Users.Count > 0)
                {
                    // The numbers changed.
                    Print(mappers);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or OsmApiException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"  The server could not be asked about '{name}': {ex.Message}");
            }
        }
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

    // By the number in the list, else by the name in the list; a mapper who
    // changed their name since is found by uid.
    private async Task<string> RemoveAsync(OptedInMappers mappers, string name, CancellationToken cancellationToken)
    {
        if (int.TryParse(name, out var number) && number >= 1 && number <= mappers.Users.Count)
        {
            return $"  Removed: {mappers.Remove(mappers.Users[number - 1].Uid)}";
        }

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
        var width = mappers.Users.Count.ToString().Length;
        for (var i = 0; i < mappers.Users.Count; i++)
        {
            Console.WriteLine($"  {(i + 1).ToString().PadLeft(width)}. {mappers.Users[i]}");
        }
    }
}
