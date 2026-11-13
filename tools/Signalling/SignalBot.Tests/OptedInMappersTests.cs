// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Osm;

namespace SignalBot.Tests;

public sealed class OptedInMappersTests
{
    [Fact]
    public void Add_keeps_one_entry_per_uid_with_the_last_name()
    {
        var mappers = new OptedInMappers([]);

        Assert.True(mappers.Add(new OsmUser(7, "old name")));
        Assert.False(mappers.Add(new OsmUser(7, "new name")));

        var user = Assert.Single(mappers.Users);
        Assert.Equal("new name", user.Name);
    }

    [Fact]
    public void Remove_finds_a_mapper_by_name_or_uid()
    {
        var mappers = new OptedInMappers([new OsmUser(1, "Alice"), new OsmUser(2, "Bob")]);

        Assert.Equal(1, mappers.Remove("alice")?.Uid);
        Assert.Equal(2, mappers.Remove(2)?.Uid);
        Assert.Null(mappers.Remove("Carol"));
        Assert.Empty(mappers.Users);
    }

    [Fact]
    public void Store_writes_and_reads_the_list()
    {
        var folder = Directory.CreateTempSubdirectory("signalbot-").FullName;
        try
        {
            var store = new OptedInMapperStore(Path.Combine(folder, "profile", "opted-in-FR.json"));
            Assert.Empty(store.Load().Users);

            store.Save(new OptedInMappers([new OsmUser(42, "Noël Danjou")]));

            var user = Assert.Single(store.Load().Users);
            Assert.Equal(new OsmUser(42, "Noël Danjou"), user);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task FindUser_reads_the_uid_from_a_changeset_of_the_mapper()
    {
        var server = new FakeOsmServer();
        server.Add(1, 1, TestBot.Legacy, user: "willing mapper");
        var api = new OsmApiClient(server.CreateClient(), new LimitSettings { NodesPerRequest = 50 });

        Assert.Equal(new OsmUser(FakeOsmServer.UidOf("willing mapper"), "willing mapper"), await api.FindUserAsync("willing mapper", CancellationToken.None));
        Assert.Null(await api.FindUserAsync("nobody", CancellationToken.None));
    }
}
