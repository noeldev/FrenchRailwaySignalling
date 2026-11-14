// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

public enum SignalScope
{
    // Every signal of the country.
    Country,

    // The signals of some areas (regions, departments...).
    Areas,

    // The signals last edited by the mappers who opted in.
    OptedInMappers,

    // The test nodes of the last seed, on a sandbox.
    TestNodes,
}

// The signals the operator chose to work on in the interactive session, kept
// from one step to the next so that plan and apply cover the same signals.
// Mappers holds the display names of the opted-in mappers when the choice was
// made, to name them; the command reads the list itself when it runs.
public sealed record SignalSelection(SignalScope Scope, IReadOnlyList<string> Areas, IReadOnlyList<string> Mappers)
{
    // Beyond this number, the mappers are counted, not named: the list was
    // shown in full when it was chosen.
    private const int MaxNames = 3;

    public static SignalSelection Country { get; } = new(SignalScope.Country, [], []);

    public static SignalSelection TestNodes { get; } = new(SignalScope.TestNodes, [], []);

    public static SignalSelection ForAreas(IReadOnlyList<string> codes) => new(SignalScope.Areas, codes, []);

    public static SignalSelection ForMappers(IReadOnlyList<string> names) => new(SignalScope.OptedInMappers, [], names);

    // The selection in a few words, to complete "Signals: ...". It stays
    // short however many mappers opted in.
    public string Describe(BotSettings settings) => Scope switch
    {
        SignalScope.Areas => $"{(Areas.Count == 1 ? "area" : "areas")} {string.Join(", ", Areas)}",
        SignalScope.OptedInMappers when Mappers.Count == 1 => $"last edited by the opted-in mapper {Mappers[0]}",
        SignalScope.OptedInMappers when Mappers.Count <= MaxNames => $"last edited by the {Mappers.Count} opted-in mappers ({string.Join(", ", Mappers)})",
        SignalScope.OptedInMappers => $"last edited by the {Mappers.Count} opted-in mappers",
        SignalScope.TestNodes => "the test nodes of the last seed",
        _ => $"every signal of {settings.Country.Name}",
    };

    // The options of a command limited to the selection.
    public CommandLineOptions ApplyTo(CommandLineOptions options) => Scope switch
    {
        SignalScope.Areas => options with { Areas = Areas },
        SignalScope.OptedInMappers => options with { OptedIn = true },
        _ => options,
    };
}
