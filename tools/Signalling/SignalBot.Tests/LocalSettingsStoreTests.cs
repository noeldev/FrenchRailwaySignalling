// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json.Nodes;
using SignalBot.Core;

namespace SignalBot.Tests;

public sealed class LocalSettingsStoreTests
{
    [Fact]
    public void Set_adds_the_values_and_keeps_the_others()
    {
        var folder = Directory.CreateTempSubdirectory("railwaymapbot-").FullName;
        try
        {
            var path = Path.Combine(folder, "profile", "settings.json");
            var store = new LocalSettingsStore(path);

            store.Set("dev", new Dictionary<string, string> { [LocalSettingsStore.Account] = "bot", [LocalSettingsStore.ClientId] = "abc" });
            store.Set("prod", new Dictionary<string, string> { [LocalSettingsStore.ClientId] = "123" });
            store.Set("dev", new Dictionary<string, string> { [LocalSettingsStore.Account] = "other" });

            var servers = JsonNode.Parse(File.ReadAllText(path))!["servers"]!;
            Assert.Equal("other", (string?)servers["dev"]!["account"]);
            Assert.Equal("abc", (string?)servers["dev"]!["clientId"]);
            Assert.Equal("123", (string?)servers["prod"]!["clientId"]);

            // Logging out of a server forgets its values, not those of the others.
            store.Remove("dev", [LocalSettingsStore.Account, LocalSettingsStore.ClientId]);
            store.Remove("nowhere", [LocalSettingsStore.Account]);

            servers = JsonNode.Parse(File.ReadAllText(path))!["servers"]!;
            Assert.Null(servers["dev"]!["account"]);
            Assert.Null(servers["dev"]!["clientId"]);
            Assert.Equal("123", (string?)servers["prod"]!["clientId"]);

            var tokens = new SignalBot.Auth.TokenStore(Path.Combine(folder, "profile", "tokens.json"));
            tokens.Save("dev", "token-dev");
            tokens.Save("prod", "token-prod");
            tokens.Remove("dev");
            Assert.Null(tokens.Find("dev"));
            Assert.Equal("token-prod", tokens.Find("prod"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
