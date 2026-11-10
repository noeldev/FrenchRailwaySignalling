// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RailwayMapBot.Osm;

// Reads and writes the XML documents of the OSM API 0.6. Positions keep the
// seven decimals the database stores, so a tag-only edit never moves a node.
public static class OsmXml
{
    private const string ApiVersion = "0.6";
    private const string CoordinateFormat = "0.0000000";

    public static IReadOnlyList<OsmNode> ReadNodes(XDocument document) =>
    [
        .. document.Root!.Elements("node").Select(node =>
        {
            var visible = (bool?)node.Attribute("visible") ?? true;
            return new OsmNode(
                (long)node.Attribute("id")!,
                (int)node.Attribute("version")!,
                visible,
                visible ? ReadCoordinate(node, "lat") : 0,
                visible ? ReadCoordinate(node, "lon") : 0,
                (DateTimeOffset?)node.Attribute("timestamp") ?? DateTimeOffset.UnixEpoch,
                (string?)node.Attribute("user") ?? string.Empty,
                node.Elements("tag").ToDictionary(tag => (string)tag.Attribute("k")!, tag => (string)tag.Attribute("v")!, StringComparer.Ordinal));
        }),
    ];

    // The result of an upload: old id to new id and version.
    public static IReadOnlyDictionary<long, NodeResult> ReadDiffResult(XDocument document) =>
        document.Root!.Elements("node").ToDictionary(
            node => (long)node.Attribute("old_id")!,
            node => new NodeResult((long?)node.Attribute("new_id") ?? 0, (int?)node.Attribute("new_version") ?? 0));

    public static string ReadUserName(XDocument document) =>
        (string?)document.Root!.Element("user")?.Attribute("display_name") ?? string.Empty;

    public static XDocument WriteChangeset(IReadOnlyDictionary<string, string> tags) =>
        new(new XElement("osm", new XElement("changeset", WriteTags(tags))));

    // An osmChange document that modifies the nodes, or creates them.
    public static XDocument WriteChange(long changesetId, IEnumerable<NodeWrite> nodes, bool create, string generator) =>
        new(new XElement("osmChange",
            new XAttribute("version", ApiVersion),
            new XAttribute("generator", generator),
            new XElement(create ? "create" : "modify", nodes.Select(node => new XElement("node",
                new XAttribute("id", node.Id),
                new XAttribute("version", node.Version),
                new XAttribute("changeset", changesetId),
                new XAttribute("lat", node.Lat.ToString(CoordinateFormat, CultureInfo.InvariantCulture)),
                new XAttribute("lon", node.Lon.ToString(CoordinateFormat, CultureInfo.InvariantCulture)),
                WriteTags(node.Tags))))));

    // Saves a document as UTF-8 without byte order mark.
    public static void Save(XDocument document, string path)
    {
        using var writer = XmlWriter.Create(path, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) });
        document.Save(writer);
    }

    private static IEnumerable<XElement> WriteTags(IReadOnlyDictionary<string, string> tags) =>
        tags.OrderBy(tag => tag.Key, StringComparer.Ordinal)
            .Select(tag => new XElement("tag", new XAttribute("k", tag.Key), new XAttribute("v", tag.Value)));

    private static double ReadCoordinate(XElement node, string name) =>
        double.Parse((string)node.Attribute(name)!, CultureInfo.InvariantCulture);
}
