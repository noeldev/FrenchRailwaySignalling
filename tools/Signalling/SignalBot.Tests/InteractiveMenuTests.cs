// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Osm;

namespace SignalBot.Tests;

// The menus of the interactive session, with a scripted operator: every menu
// can be left, and nothing is chosen by accident.
[Collection(ConsoleScript.Collection)]
public sealed class InteractiveMenuTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("signalbot-").FullName;
    private readonly FakeOsmServer _server = new();
    private readonly OptedInMapperStore _store;

    private readonly BotSettings _settings = new()
    {
        Country = new CountrySettings { Code = "FR", Name = "France", Selector = "area[\"ISO3166-1\"=\"FR\"]" },
        AreaKinds =
        [
            new AreaKindSettings { Name = "region", Format = "three letters", Pattern = "[A-Z]{3}", Selector = "area[\"ISO3166-2\"=\"FR-{code}\"]", Examples = "BRE, NOR" },
            new AreaKindSettings { Name = "department", Format = "two digits", Pattern = "[0-9]{2}", Selector = "area[\"ref:INSEE\"=\"{code}\"]", Examples = "14, 75" },
        ],
    };

    public InteractiveMenuTests()
    {
        _store = new OptedInMapperStore(Path.Combine(_folder, "opted-in-FR.json"));
        _server.Add(1, 1, TestBot.Legacy, user: "Alice");
        _server.Add(2, 1, TestBot.Legacy, user: "Bob");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private OptedInMapperEditor Editor => new(new OsmApiClient(_server.CreateClient(), new LimitSettings { NodesPerRequest = 50 }), _store);

    [Fact]
    public async Task Mappers_can_be_added_then_removed_before_going_on()
    {
        using var console = new ConsoleScript("opt-in", "Alice", "Bob", "nobody", "", "opt-out", "alice", "", "continue");

        var mappers = await Editor.RunAsync(CancellationToken.None);

        Assert.NotNull(mappers);
        Assert.Equal("Bob", Assert.Single(mappers.Users).Name);
        Assert.Equal("Bob", Assert.Single(_store.Load().Users).Name);
        Assert.Contains("No mapper named 'nobody'", console.Printed);
        Assert.Contains("Removed: Alice", console.Printed);
    }

    [Fact]
    public async Task Mappers_menu_offers_to_go_on_only_with_a_mapper_and_goes_on_by_default()
    {
        using var console = new ConsoleScript("continue", "", "Alice", "", "");

        var mappers = await Editor.RunAsync(CancellationToken.None);

        // "continue" is not a choice of an empty list: asked again, then Enter
        // takes opt-in; once Alice is in, Enter takes continue.
        Assert.Equal("Alice", Assert.Single(mappers!.Users).Name);
    }

    [Fact]
    public async Task Mappers_menu_can_be_left_and_keeps_the_list()
    {
        _store.Save(new OptedInMappers([new OsmUser(7, "Carol")]));
        using var console = new ConsoleScript("back");

        Assert.Null(await Editor.RunAsync(CancellationToken.None));
        Assert.Equal("Carol", Assert.Single(_store.Load().Users).Name);
    }

    [Fact]
    public async Task Signals_menu_goes_back_from_areas_and_mappers_without_choosing()
    {
        using var console = new ConsoleScript("area", "XX1", "", "mapper", "back", "area", "bre, 14", "ignored");

        var selection = await new SignalSelector(_settings, Editor).AskAsync(current: null, CancellationToken.None);

        Assert.Equal(SignalScope.Areas, selection!.Scope);
        Assert.Equal(new[] { "BRE", "14" }, selection.Areas);
        Assert.Contains("Unknown area code: XX1", console.Printed);
    }

    [Fact]
    public async Task Signals_menu_keeps_the_current_choice_by_default()
    {
        using var console = new ConsoleScript("");

        Assert.Null(await new SignalSelector(_settings, Editor).AskAsync(SignalSelection.ForAreas(["BRE"]), CancellationToken.None));
        Assert.Contains("keep the signals of area BRE", console.Printed);
    }

    [Fact]
    public async Task Signals_menu_takes_the_opted_in_mappers()
    {
        using var console = new ConsoleScript("mapper", "opt-in", "Alice", "", "continue");

        var selection = await new SignalSelector(_settings, Editor).AskAsync(current: null, CancellationToken.None);

        Assert.Equal(SignalScope.OptedInMappers, selection!.Scope);
        Assert.True(selection.ApplyTo(new CommandLineOptions { Command = BotCommand.Plan }).OptedIn);
        Assert.Equal("the signals last edited by the opted-in mapper (Alice)", selection.Describe(_settings));
    }

    // A script that ends, or Ctrl+Z: the question is not asked forever.
    [Fact]
    public async Task A_closed_input_cancels_the_question()
    {
        using var console = new ConsoleScript("opt-in");

        await Assert.ThrowsAsync<OperationCanceledException>(() => Editor.RunAsync(CancellationToken.None));
    }
}
