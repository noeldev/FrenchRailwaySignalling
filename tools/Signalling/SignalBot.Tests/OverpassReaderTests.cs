// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using SignalBot.Osm;

namespace SignalBot.Tests;

public sealed class OverpassReaderTests
{
    [Fact]
    public void Read_gives_the_nodes_and_the_name_of_the_area()
    {
        const string json = """
            {
              "elements": [
                { "type": "area", "id": 3600007453, "tags": { "name": "Calvados", "ref:INSEE": "14" } },
                { "type": "node", "id": 1, "lat": 49.1, "lon": -0.3, "version": 3, "user": "mapper", "tags": { "railway": "signal" } }
              ]
            }
            """;

        var answer = OverpassReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("Calvados", answer.AreaName);
        Assert.Equal(3, Assert.Single(answer.Nodes).Version);
    }
}
