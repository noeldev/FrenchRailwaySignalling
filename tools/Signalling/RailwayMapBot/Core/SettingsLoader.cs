// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Microsoft.Extensions.Configuration;

namespace RailwayMapBot.Core;

// Reads the settings, each source over the previous one:
// 1. appsettings.json, next to the program (the shipped settings);
// 2. the --settings file, if any (another country, another scheme);
// 3. the settings of the operator in the user profile (accounts, client ids,
//    approved rule tables), written by login and approve.
// Read again before every command, so what login or approve wrote is used at
// once.
public static class SettingsLoader
{
    public static BotSettings Load(string? settingsPath)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        if (settingsPath is not null)
        {
            AddFile(builder, Path.GetFullPath(settingsPath));
        }

        if (File.Exists(UserProfile.SettingsPath))
        {
            AddFile(builder, UserProfile.SettingsPath);
        }

        var settings = builder.Build().Get<BotSettings>() ?? new BotSettings();
        var errors = SettingsValidator.Validate(settings);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid settings:" + string.Concat(errors.Select(error => $"{Environment.NewLine}  - {error}")));
        }

        return settings;
    }

    // A file outside the folder of the program: without a file provider, the
    // configuration reads it from its own folder.
    private static void AddFile(IConfigurationBuilder builder, string fullPath) =>
        builder.AddJsonFile(provider: null, fullPath, optional: false, reloadOnChange: false);
}
