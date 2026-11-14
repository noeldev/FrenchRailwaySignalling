// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;
using System.Text.Json.Nodes;

namespace SignalBot.Core;

// Writes a settings file of the operator (see UserProfile), read over the
// shipped settings: the same layout, holding only the values of this machine,
// for example
//   { "servers": { "dev": { "account": "...", "clientId": "..." } } }
// Values written by hand are kept.
public sealed class LocalSettingsStore(string path)
{
    public const string Account = "account";
    public const string ClientId = "clientId";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Set(string server, IReadOnlyDictionary<string, string> values)
    {
        var root = Load();
        var servers = Child(root, "servers");
        var entry = Child(servers, server);
        foreach (var (key, value) in values)
        {
            entry[key] = value;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(Options));
    }

    // Forgets the values of a server; the others are kept.
    public void Remove(string server, IEnumerable<string> keys)
    {
        var root = Load();
        if (root["servers"]?[server] is not JsonObject entry)
        {
            return;
        }

        foreach (var key in keys)
        {
            entry.Remove(key);
        }

        File.WriteAllText(path, root.ToJsonString(Options));
    }

    private JsonObject Load() =>
        File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];

    private static JsonObject Child(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject child)
        {
            return child;
        }

        child = [];
        parent[name] = child;
        return child;
    }
}
