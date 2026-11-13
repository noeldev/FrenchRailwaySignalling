// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;
using SignalBot.Osm;

namespace SignalBot.Core;

// Reads and writes the opted-in mappers of a country, kept in the user
// profile (see UserProfile): the identifiers of mappers are never committed.
// The file is replaced as a whole, so an interrupted save never leaves half
// a list behind.
public sealed class OptedInMapperStore(string path)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public OptedInMappers Load() =>
        new(File.Exists(path) ? JsonSerializer.Deserialize<List<OsmUser>>(File.ReadAllText(path), Options) ?? [] : []);

    public void Save(OptedInMappers mappers)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(mappers.Users, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
