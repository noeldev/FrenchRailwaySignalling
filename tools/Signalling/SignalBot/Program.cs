// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using SignalBot.Core;

TrySetUtf8Output();

Banner.Print();

using var stopSignal = new StopSignal();
var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());

try
{
    // Without a command: the interactive session.
    if (args.Length == 0)
    {
        return await new InteractiveSession(repositoryRoot, stopSignal).RunAsync();
    }

    var options = CommandLineOptions.Parse(args);
    if (options is null)
    {
        return 1;
    }

    var settings = SettingsLoader.Load(CountryProfiles.Resolve(options.Country));
    return await new BotApplication(settings, options, repositoryRoot).RunAsync(stopSignal.Start(options.Writes));
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
