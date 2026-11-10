// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;

namespace RailwayMapBot.Auth;

// Keeps the access token of each server in the profile of the user, outside
// the repository, so it is never committed.
public sealed class TokenStore(string path)
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RailwayMapBot", "tokens.json");

    public string? Find(string server) => Load().GetValueOrDefault(server);

    public void Save(string server, string token)
    {
        var tokens = Load();
        tokens[server] = token;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(tokens));
    }

    private Dictionary<string, string> Load() =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? []
            : [];
}
