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

    [Fact]
    public void Read_gives_the_time_of_the_data()
    {
        var answer = Read("""{ "osm3s": { "timestamp_osm_base": "2026-11-13T21:58:12Z" }, "elements": [] }""");

        Assert.Equal(new DateTimeOffset(2026, 11, 13, 21, 58, 12, TimeSpan.Zero), answer.DataTime);
        Assert.Empty(answer.Nodes);
    }

    // Overpass answers a query it could not complete with what it found so
    // far and a remark: a partial list must never be taken for the list.
    [Fact]
    public void Read_refuses_an_answer_the_server_did_not_complete()
    {
        const string json = """
            {
              "elements": [ { "type": "node", "id": 1, "lat": 49.1, "lon": -0.3 } ],
              "remark": "runtime error: Query timed out in \"query\" at line 4 after 601 seconds."
            }
            """;

        var error = Throws(() => Read(json));

        Assert.True(error.IsTransient);
        Assert.Contains("Query timed out", error.Message);
    }

    [Fact]
    public void Read_refuses_a_truncated_answer_and_an_error_page()
    {
        Assert.True(Throws(() => Read("""{ "elements": [ { "type": "node", "id": 1, "lat": 49.1, """)).IsTransient);
        Assert.True(Throws(() => Read("<html><body>Gateway Timeout</body></html>")).IsTransient);
        Assert.True(Throws(() => Read("[]")).IsTransient);
        Assert.True(Throws(() => Read("""{ "elements": [ { "type": "node", "id": "one" } ] }""")).IsTransient);
    }

    private static OverpassAnswer Read(string json) => OverpassReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private static OverpassException Throws(Action action)
    {
        try
        {
            action();
        }
        catch (OverpassException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("No OverpassException was thrown.");
    }
}
