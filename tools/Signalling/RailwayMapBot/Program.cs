// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using Microsoft.Extensions.Configuration;
using RailwayMapBot.Core;

TrySetUtf8Output();

Banner.Print();

var options = CommandLineOptions.Parse(args);
if (options is null)
{
    return 1;
}

try
{
    var settings = LoadConfiguration(options.SettingsPath).Get<BotSettings>() ?? new BotSettings();
    var errors = SettingsValidator.Validate(settings);
    if (errors.Count > 0)
    {
        Console.Error.WriteLine("Error: invalid settings.");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }

        return 1;
    }

    // Ctrl+C asks the run to stop after the current batch, changeset closed.
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        Console.WriteLine("Stopping after the current batch...");
        stop.Cancel();
    };

    return await new BotApplication(settings, options, FindRepositoryRoot(Directory.GetCurrentDirectory())).RunAsync(stop.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Stopped by the operator.");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static IConfiguration LoadConfiguration(string? settingsPath)
{
    var builder = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        // Machine-specific overrides (accounts, client ids), kept out of the repository.
        .AddJsonFile("appsettings.local.json", optional: true);

    if (settingsPath is not null)
    {
        builder.AddJsonFile(Path.GetFullPath(settingsPath), optional: false);
    }

    return builder.Build();
}

// Nearest parent containing a .git folder, so the tool can be launched from
// anywhere in the repository. Falls back to the current directory.
static string FindRepositoryRoot(string startDirectory)
{
    DirectoryInfo? current = new(startDirectory);

    while (current != null)
    {
        if (Directory.Exists(Path.Combine(current.FullName, ".git")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return startDirectory;
}

static void TrySetUtf8Output()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // Output is redirected to a handle that rejects encoding changes.
    }
}
