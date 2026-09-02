// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using SignalAudit.Core;
using SignalAudit.Validators;

// The banner and some messages contain non-ASCII characters, so make sure the
// console can render them regardless of the active code page.
TrySetUtf8Output();

Banner.Print();

var configuration = LoadConfiguration();
var sources = configuration.GetSection("Sources").Get<SourcesSettings>() ?? new SourcesSettings();

var options = CommandLineOptions.Parse(args, sources);
if (options is null)
{
    return 1;
}

if (!File.Exists(options.XmlPath))
{
    Console.Error.WriteLine($"Preset file not found: {options.XmlPath}");
    return 1;
}

XDocument document;
try
{
    // Line information lets validators point issues back to the source file.
    document = XDocument.Load(options.XmlPath, LoadOptions.SetLineInfo);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Unable to load XML: {ex.Message}");
    return 1;
}

Console.WriteLine($"Validating: {Path.GetFullPath(options.XmlPath)}");

var context = new ValidationContext(options, document);

var validators = new List<IValidator>
{
    new XsdSchemaValidator(),
    new ChunkValidator(),
    new IconValidator(),
    new ItemLinkValidator(),
    new StructuralValidator(),
    new MenuPathValidator(),
    new MatchExpressionValidator()
};

// --wiki enables the link/anchor integrity check, and the content sync when a
// source is available. --yaml enables the map sync.
if (options.WikiRequested)
{
    validators.Add(new WikiLinkValidator());

    if (options.WikiSyncSource is not null)
    {
        validators.Add(new WikiSyncValidator());
    }
}

if (options.OrmVectorYamlSource is not null)
{
    validators.Add(new RenderSyncValidator());
}

var utf8NoBom = new UTF8Encoding(false);
if (options.LogPath is not null)
{
    EnsureParentDirectory(options.LogPath);
}

using var transcript = options.LogPath is null ? null : new StreamWriter(options.LogPath, false, utf8NoBom);

var reporter = new ConsoleReporter(transcript);
var errorCount = 0;

foreach (var validator in validators)
{
    reporter.BeginValidator(validator.Name);

    IReadOnlyList<ValidationIssue> issues;
    try
    {
        issues = await validator.ValidateAsync(context, CancellationToken.None);
    }
    catch (Exception ex)
    {
        issues = [new ValidationIssue(ValidationSeverity.Error, $"Validator crashed: {ex.Message}")];
    }

    errorCount += reporter.Report(issues);
}

reporter.ReportSummary(errorCount);

if (options.JsonReportPath is not null)
{
    EnsureParentDirectory(options.JsonReportPath);
    JsonReportWriter.Write(options.JsonReportPath, reporter.Reports);
    Console.WriteLine($"Wrote report: {Path.GetFullPath(options.JsonReportPath)}");
}

return errorCount > 0 ? 1 : 0;

static void EnsureParentDirectory(string path)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(path));
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
}

static void TrySetUtf8Output()
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // Output is redirected to a handle that rejects encoding changes; the
        // banner still works, only some accented characters may look wrong.
    }
}

static IConfiguration LoadConfiguration()
{
    return new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        // Machine-specific overrides (local paths), kept out of the repository.
        .AddJsonFile("appsettings.local.json", optional: true)
        .Build();
}
