// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using MapRouletteGen.Challenge;
using MapRouletteGen.Core;
using MapRouletteGen.Osm;
using Microsoft.Extensions.Configuration;
using Signalling.Rules;

TrySetUtf8Output();

Banner.Print();

var settings = LoadConfiguration().Get<GeneratorSettings>() ?? new GeneratorSettings();
var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());

var options = CommandLineOptions.Parse(args, settings, repositoryRoot);
if (options is null)
{
    return 1;
}

try
{
    Console.WriteLine($"Reading rules from '{options.RulesPath}'...");
    var table = RuleTable.Load(options.RulesPath);

    Console.WriteLine($"Reading nodes from '{options.InputPath}'...");
    var nodes = OverpassReader.Read(options.InputPath);

    var content = new ChallengeBuilder(table).Build(nodes);
    ChallengeFileWriter.Write(options.OutputPath, content.Tasks);
    ChallengeTextWriter.Write(options.TextPath, settings.Challenge, content, Path.GetFileName(options.InputPath));

    Console.WriteLine($"Checked {nodes.Count} nodes: {content.Tasks.Count} with legacy tagging.");
    foreach (var (rule, count) in content.RuleCounts)
    {
        Console.WriteLine($"  {count,6}  {rule}");
    }

    if (content.ConflictCounts.Count > 0)
    {
        Console.WriteLine($"Conflicts left to the JOSM validator: {content.ConflictCounts.Values.Sum()}.");
    }

    Console.WriteLine($"Wrote '{options.OutputPath}' and '{options.TextPath}'.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: generation failed - {ex.Message}");
    return 1;
}

static IConfiguration LoadConfiguration()
{
    return new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        // Machine-specific overrides (local paths), kept out of the repository.
        .AddJsonFile("appsettings.local.json", optional: true)
        .Build();
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
