// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Signalling.Rules;
using Signalling.Schema;
using ValidatorGen.Core;
using ValidatorGen.Generation;
using ValidatorGen.Rules;

TrySetUtf8Output();

Banner.Print();

var settings = LoadConfiguration().Get<GeneratorSettings>() ?? new GeneratorSettings();
var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());

var options = CommandLineOptions.Parse(args, settings.Validator, repositoryRoot);
if (options is null)
{
    return 1;
}

try
{
    using var client = CreateHttpClient(options.TimeoutSeconds);
    var pages = await new WikiPageReader(client, repositoryRoot).ReadAsync(settings.Wiki, options.UseLocalPages, CancellationToken.None);
    foreach (var page in pages)
    {
        Console.WriteLine($"Read {page.Role} page {page.Title} ({page.Timestamp.UtcDateTime:yyyy-MM-dd HH:mm} UTC) from {page.Origin}");
    }

    var schema = TagSchemaBuilder.Build(pages, settings.Validator.KeyPrefix, settings.Validator.CountryPrefix);
    var closedCount = schema.Keys.Count(spec => !spec.IsOpen);
    Console.WriteLine($"Documented keys: {schema.Keys.Count} ({closedCount} with a closed value list).");

    Console.WriteLine($"Reading rules from '{options.RulesPath}'...");
    var table = RuleTable.Load(options.RulesPath);
    var errors = RuleTableValidator.Validate(table, schema, settings.Validator.KeyPrefix);
    if (errors.Count > 0)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"Error: {error}");
        }

        Console.Error.WriteLine($"{errors.Count} rule table error(s); nothing written.");
        return 1;
    }

    var rules = new ValidatorRuleBuilder(schema, table, settings.Validator).Build();
    var version = ValidatorFileWriter.Write(options.OutputPath, rules, pages, settings.Validator);

    Console.WriteLine(
        $"Wrote '{options.OutputPath}' version {version}: "
        + $"{table.ValueReplacements.Count} value replacements, {table.TagReplacements.Count} tag replacements, "
        + $"{table.Conflicts.Count} conflicts.");
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
static HttpClient CreateHttpClient(int timeoutSeconds)
{
    var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
    var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
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
