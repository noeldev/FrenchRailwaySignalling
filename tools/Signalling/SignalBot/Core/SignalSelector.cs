// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// The "Signals" menu of the interactive session, on the real map: every
// signal of the country, those of areas, or those last edited by the mappers
// who opted in (see OptedInMapperEditor). Each choice can be left without
// choosing anything, back to this menu, and the menu itself can be left.
public sealed class SignalSelector(BotSettings settings, OptedInMapperEditor mapperEditor)
{
    private const string All = "all";
    private const string Area = "area";
    private const string Mapper = "mapper";
    private const string Back = "back";

    // The signals chosen, or null when the operator left the menu: the
    // current selection is then kept, or the server is chosen again when
    // there is none yet.
    public async Task<SignalSelection?> AskAsync(SignalSelection? current, CancellationToken cancellationToken)
    {
        while (true)
        {
            Console.WriteLine("Signals:");
            var choice = ConsolePrompt.Choose("Signals",
            [
                (All, $"every signal of {settings.Country.Name}"),
                (Area, "the signals of regions or departments"),
                (Mapper, "the signals last edited by the mappers who opted in"),
                (Back, current is null ? "back to the choice of the server" : $"keep {current.Describe(settings)}"),
            ], current is null ? All : Back);

            switch (choice)
            {
                case All:
                    return SignalSelection.Country;

                case Area when AskAreas() is { } codes:
                    return SignalSelection.ForAreas(codes);

                case Mapper when await mapperEditor.RunAsync(cancellationToken) is { } mappers:
                    return SignalSelection.ForMappers([.. mappers.Users.Select(user => user.Name)]);

                case Back:
                    return null;
            }

            Console.WriteLine();
        }
    }

    // The codes typed, checked at once and normalized; null to go back.
    private List<string>? AskAreas()
    {
        var resolver = new AreaResolver(settings);
        Console.WriteLine(AreaResolver.Describe(settings.AreaKinds));
        while (true)
        {
            var answer = ConsolePrompt.Ask("Areas, comma-separated (Enter to go back)");
            if (answer.Length == 0)
            {
                return null;
            }

            try
            {
                var codes = answer.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return [.. resolver.Resolve(codes).Select(area => area.Code).Distinct(StringComparer.Ordinal)];
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
