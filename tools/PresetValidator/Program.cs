// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Xml.Linq;
using PresetValidator.Core;
using PresetValidator.Validators;

// The banner and some messages contain non-ASCII characters, so make sure the
// console can render them regardless of the active code page.
TrySetUtf8Output();

Banner.Print();

var options = CommandLineOptions.Parse(args);
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

if (options.CheckWiki)
{
    validators.Add(new WikiLinkValidator());
}

var reporter = new ConsoleReporter();
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
return errorCount > 0 ? 1 : 0;

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
