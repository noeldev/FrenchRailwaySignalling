// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// Files of the operator, kept in the user profile (%APPDATA%\SignalBot on
// Windows), outside the repository, so they are never committed. The tool
// writes them itself (login, the opted-in mappers of the interactive
// session): nothing to create or edit by hand.
public static class UserProfile
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "SignalBot");

    // Accounts and client ids of the servers, read over appsettings.json.
    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    // Mappers of a country who asked for their signals to be fixed.
    public static string OptedInMappersPath(string countryCode) => Path.Combine(Folder, $"opted-in-{countryCode}.json");

    // Access tokens of the servers.
    public static string TokensPath => Path.Combine(Folder, "tokens.json");
}
