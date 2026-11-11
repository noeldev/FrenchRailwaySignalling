// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;

namespace SignalBot.Tests;

public sealed class AreaResolverTests
{
    private static readonly BotSettings Settings = new()
    {
        AreaKinds =
        [
            new AreaKindSettings { Name = "region", Pattern = "[A-Z]{3}|20R", Selector = "area[\"ISO3166-2\"=\"FR-{code}\"]", Examples = "BRE" },
            new AreaKindSettings { Name = "department", Pattern = "[0-9]{2,3}|2[AB]", Selector = "area[\"ref:INSEE\"=\"{code}\"]", Examples = "14" },
        ],
        Country = new CountrySettings { Code = "FR", Name = "France", Selector = "area[\"ISO3166-1\"=\"FR\"]" },
    };

    [Fact]
    public void Resolve_recognizes_regions_and_departments()
    {
        var resolver = new AreaResolver(Settings);

        var region = resolver.Resolve("bre");
        var department = resolver.Resolve("2a");

        Assert.Equal(new ResolvedArea("BRE", "area[\"ISO3166-2\"=\"FR-BRE\"]", null), region);
        Assert.Equal(new ResolvedArea("2A", "area[\"ref:INSEE\"=\"2A\"]", null), department);
    }

    [Fact]
    public void Resolve_gives_the_whole_country_when_no_code_is_given()
    {
        var area = Assert.Single(new AreaResolver(Settings).Resolve([]));

        Assert.Equal(new ResolvedArea("FR", "area[\"ISO3166-1\"=\"FR\"]", "France"), area);
    }

    [Fact]
    public void Resolve_rejects_an_unknown_kind_of_code()
    {
        var resolver = new AreaResolver(Settings);

        var error = Record(() => resolver.Resolve("FR-BRE"));

        Assert.StartsWith("Unknown area code: FR-BRE.", error);
    }

    private static string? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
