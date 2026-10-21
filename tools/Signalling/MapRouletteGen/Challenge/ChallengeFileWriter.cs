// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MapRouletteGen.Challenge;

// Writes the tasks as a MapRoulette tag fix challenge: line-by-line GeoJSON,
// one FeatureCollection per task, each record preceded by the RFC 7464 record
// separator (required by MapRoulette since v3.6.5). The cooperativeWork
// section (meta version 2, type 1) holds the tag changes MapRoulette offers to
// apply; the feature properties carry the current tags, the element reference
// the legacy tagging and mistyped values found (legacy_rules) and what is left
// to do by hand (legacy_notes), both for the instruction.
// References:
//   https://github.com/maproulette/maproulette3/wiki/Cooperative-Challenges
//   https://learn.maproulette.org/en-US/documentation/line-by-line-geojson/
public static class ChallengeFileWriter
{
    private const char RecordSeparator = '\u001e';
    private const string LegacyRulesProperty = "legacy_rules";
    private const string LegacyNotesProperty = "legacy_notes";

    // Accented values are written as is, not as \u escapes.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Write(string path, IReadOnlyList<LegacyTask> tasks)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Line-by-line GeoJSON: one record per line, LF as MapRoulette expects.
        using var output = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (var task in tasks)
        {
            output.Write(RecordSeparator);
            output.WriteLine(ToJson(task));
        }
    }

    private static string ToJson(LegacyTask task)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "FeatureCollection");

            writer.WriteStartArray("features");
            WriteFeature(writer, task);
            writer.WriteEndArray();

            writer.WriteStartObject("cooperativeWork");
            writer.WriteStartObject("meta");
            writer.WriteNumber("version", 2);
            writer.WriteNumber("type", 1);
            writer.WriteEndObject();
            writer.WriteStartArray("operations");
            WriteOperation(writer, task);
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteFeature(Utf8JsonWriter writer, LegacyTask task)
    {
        var node = task.Node;
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");
        writer.WriteString("id", node.Reference);

        writer.WriteStartObject("geometry");
        writer.WriteString("type", "Point");
        writer.WriteStartArray("coordinates");
        writer.WriteNumberValue(node.Lon);
        writer.WriteNumberValue(node.Lat);
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartObject("properties");
        writer.WriteString("@id", node.Reference);
        foreach (var (key, value) in node.Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
        {
            writer.WriteString(key, value);
        }

        writer.WriteString(LegacyRulesProperty, string.Join(", ", task.Findings));
        writer.WriteString(LegacyNotesProperty, string.Join(" ", task.Fix.Notes.Select(note => char.ToUpperInvariant(note[0]) + note[1..] + ".")));
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteOperation(Utf8JsonWriter writer, LegacyTask task)
    {
        writer.WriteStartObject();
        writer.WriteString("operationType", "modifyElement");
        writer.WriteStartObject("data");
        writer.WriteString("id", task.Node.Reference);
        writer.WriteStartArray("operations");

        var setTags = task.Fix.SetTags;
        if (setTags.Count > 0)
        {
            writer.WriteStartObject();
            writer.WriteString("operation", "setTags");
            writer.WriteStartObject("data");
            foreach (var (key, value) in setTags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        var unsetKeys = task.Fix.UnsetKeys;
        if (unsetKeys.Count > 0)
        {
            writer.WriteStartObject();
            writer.WriteString("operation", "unsetTags");
            writer.WriteStartArray("data");
            foreach (var key in unsetKeys.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(key);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
