// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SignalBot.Reporting;

// Append-only journal of a run, one JSON object per line, flushed after every
// line. It is the audit trail of the run and the data needed to revert it.
public sealed class RunJournal(string path) : IDisposable
{
    public const string FileName = "journal.jsonl";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly StreamWriter _writer = new(path, append: false) { AutoFlush = true };

    public void Write(JournalRecord record) => _writer.WriteLine(JsonSerializer.Serialize(record, Options));

    public void Dispose() => _writer.Dispose();

    // The run of a journal and the edits it sent to the server, by area. A
    // batch decided again after a conflict replaces its first writing.
    public static (RunInfo Run, IReadOnlyList<KeyValuePair<string, JournalEdit>> Edits) ReadUploads(string path)
    {
        RunInfo? run = null;
        SortedDictionary<int, JournalRecord> batches = [];

        // The file is a sequence of JSON objects, written one per line (JSON
        // Lines). It is read as a sequence, wherever the line breaks are, so a
        // journal reformatted and saved by an editor can still be reverted.
        var content = File.ReadAllBytes(path).AsSpan();
        if (content.StartsWith(Encoding.UTF8.Preamble))
        {
            content = content[Encoding.UTF8.Preamble.Length..];
        }

        var reader = new Utf8JsonReader(content, new JsonReaderOptions { AllowMultipleValues = true });
        while (reader.Read())
        {
            var record = JsonSerializer.Deserialize<JournalRecord>(ref reader, Options)
                ?? throw new InvalidDataException($"Empty journal record in {path}");

            run ??= record.Run;
            if (record is { Type: "batch", Batch: { } batch, Changeset: not null })
            {
                batches[batch] = record;
            }
        }

        if (run is null)
        {
            throw new InvalidDataException($"Not a run journal (no run record): {path}");
        }

        return (run, [.. batches.Values.SelectMany(batch => batch.Edits!.Select(edit => KeyValuePair.Create(batch.Area!, edit)))]);
    }
}
