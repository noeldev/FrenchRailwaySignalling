// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Globalization;
using System.Text;
using System.Xml;

namespace MapRouletteGen.Challenge;

// Writes one sample node per correction, with its original tags, as an OSM
// file to open in JOSM and check what the validator reports and how its fixes
// apply. The nodes are new (negative ids) and the file cannot be uploaded; a
// comment before each node names the correction it illustrates.
public static class SampleFileWriter
{
    public static void Write(string path, ChallengeContent content, string generator)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            NewLineChars = "\r\n",
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("osm");
        writer.WriteAttributeString("version", "0.6");
        writer.WriteAttributeString("generator", generator);
        writer.WriteAttributeString("upload", "never");

        var id = 0;
        foreach (var (label, node) in content.Samples)
        {
            writer.WriteComment($" {label.Replace("--", "- -", StringComparison.Ordinal)} (node {node.Id}) ");
            writer.WriteStartElement("node");
            writer.WriteAttributeString("id", (--id).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("lat", node.Lat.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("lon", node.Lon.ToString("R", CultureInfo.InvariantCulture));

            foreach (var (key, value) in node.Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
            {
                writer.WriteStartElement("tag");
                writer.WriteAttributeString("k", key);
                writer.WriteAttributeString("v", value);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }
}
