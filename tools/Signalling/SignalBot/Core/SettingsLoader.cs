// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Microsoft.Extensions.Configuration;

namespace SignalBot.Core;

// Reads the settings of one country, each source over the previous one:
// 1. appsettings.json, next to the program: what does not depend on a country;
// 2. the profile of the country (countries/<code>.json);
// 3. the settings of the operator (accounts, client ids), written by login;
// 4. the settings of the operator for the country (approved rule tables),
//    written by approve.
// Read again before every command, so what login or approve wrote is used at
// once.
public static class SettingsLoader
{
    public static BotSettings Load(string countryCode)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        AddFile(builder, CountryProfiles.PathOf(countryCode));
        foreach (var path in new[] { UserProfile.SettingsPath, UserProfile.CountrySettingsPath(countryCode) }.Where(File.Exists))
        {
            AddFile(builder, path);
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
