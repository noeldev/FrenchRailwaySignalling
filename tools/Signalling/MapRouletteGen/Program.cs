// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Text;
using MapRouletteGen.Challenge;
using MapRouletteGen.Core;
using MapRouletteGen.Osm;
using Microsoft.Extensions.Configuration;
using Signalling.Rules;
using Signalling.Schema;

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
    using var client = CreateHttpClient();
    var pages = await new WikiPageReader(client, repositoryRoot).ReadAsync(settings.Wiki, options.UseLocalPages, CancellationToken.None);
    foreach (var page in pages)
    {
        Console.WriteLine($"Read {page.Role} page {page.Title} ({page.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm} UTC) from {page.Origin}");
    }

    var schema = TagSchemaBuilder.Build(pages, settings.KeyPrefix, settings.CountryPrefix);

    Console.WriteLine($"Reading rules from '{options.RulesPath}'...");
    var table = RuleTable.Load(options.RulesPath);

    Console.WriteLine($"Reading nodes from '{options.InputPath}'...");
    var nodes = OverpassReader.Read(options.InputPath);

    var content = new ChallengeBuilder(table, schema, settings.KeyPrefix, settings.CountryPrefix).Build(nodes);
    ChallengeFileWriter.Write(options.OutputPath, content.Tasks);
    ChallengeTextWriter.Write(options.TextPath, settings.Challenge, content, Path.GetFileName(options.InputPath));
    SampleFileWriter.Write(options.SamplesPath, content, Banner.ProductVersion());

    Console.WriteLine($"Checked {nodes.Count} nodes: {content.Tasks.Count} tasks.");
    Console.WriteLine($"  Legacy tagging fixed: {content.RuleCounts.Sum(entry => entry.Value)} ({content.RuleCounts.Count} rules).");
    Console.WriteLine($"  Mistyped values fixed: {content.TokenFixCounts.Sum(entry => entry.Value)}.");
    Console.WriteLine($"  Left to fix by hand: {content.BlockedCounts.Sum(entry => entry.Value)} blocked replacements, "
        + $"{content.ConflictCounts.Sum(entry => entry.Value)} conflicts, "
        + $"{content.UndocumentedCounts.Sum(entry => entry.Value)} undocumented keys or values.");
    Console.WriteLine($"Wrote '{options.OutputPath}', '{options.TextPath}' and '{options.SamplesPath}' ({content.Samples.Count} samples).");
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

// Wikimedia sites require an identifiable User-Agent.
static HttpClient CreateHttpClient()
{
    var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
    var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"{Banner.ProductVersion()} (+https://github.com/noeldev/FrenchRailwaySignalling)");
    return client;
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
