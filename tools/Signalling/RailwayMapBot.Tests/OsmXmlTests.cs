// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using RailwayMapBot.Osm;

namespace RailwayMapBot.Tests;

public sealed class OsmXmlTests
{
    [Fact]
    public void ReadNodes_reads_the_metadata_and_a_deleted_node()
    {
        var document = XDocument.Parse(
            """
            <osm version="0.6">
              <node id="1" visible="true" version="5" changeset="9" timestamp="2026-10-20T05:55:51Z" user="Noël" uid="2" lat="48.7233392" lon="-3.4618886">
                <tag k="railway" v="signal"/>
              </node>
              <node id="2" visible="false" version="3" changeset="10" timestamp="2026-10-21T00:00:00Z" user="other" uid="3"/>
            </osm>
            """);

        var nodes = OsmXml.ReadNodes(document);

        Assert.Equal(5, nodes[0].Version);
        Assert.Equal("Noël", nodes[0].User);
        Assert.Equal(-3.4618886, nodes[0].Lon);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 5, 55, 51, TimeSpan.Zero), nodes[0].Timestamp);
        Assert.Equal("signal", nodes[0].Tags["railway"]);
        Assert.False(nodes[1].Visible);
    }

    [Fact]
    public void WriteChange_sends_the_version_the_whole_tags_and_seven_decimals()
    {
        var node = new NodeWrite(1, 5, 48.7233392, -3.46, TestBot.Tags(("railway", "signal"), ("ref", "a \"b\" <c>")));

        var change = OsmXml.WriteChange(77, [node], create: false, "RailwayMapBot/1.0.0");
        var written = XDocument.Parse(change.ToString()).Root!.Element("modify")!.Element("node")!;

        Assert.Equal("5", (string)written.Attribute("version")!);
        Assert.Equal("77", (string)written.Attribute("changeset")!);
        Assert.Equal("48.7233392", (string)written.Attribute("lat")!);
        Assert.Equal("-3.4600000", (string)written.Attribute("lon")!);
        Assert.Equal(2, written.Elements("tag").Count());
        Assert.Equal("a \"b\" <c>", (string)written.Elements("tag").Single(tag => (string)tag.Attribute("k")! == "ref").Attribute("v")!);
    }
}
