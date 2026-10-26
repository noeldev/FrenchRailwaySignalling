// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.OrmVector;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Checks that every icon the ORM-vector map YAML references for France
// actually exists on disk. The YAML and its icons follow a fixed layout: the
// YAML lives in a "features" folder, and "symbols" is its sibling, with
// French icons under "symbols/fr". This only runs against a local YAML file;
// a URL source has no local "symbols" folder to check against, so the check
// is skipped rather than guessed at.
public sealed class MapIconValidator : IValidator
{
    private const string Country = "FR";
    private const string SymbolsFolderName = "symbols";
    private const string IconExtension = ".svg";

    public string Name => "Map icons";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var source = context.Options.OrmVectorYamlSource;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(
                [new ValidationIssue(ValidationSeverity.Info, "No ORM-vector YAML source; icon check skipped.")]);
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(
                [new ValidationIssue(ValidationSeverity.Info, "ORM-vector YAML source is a URL; icon check needs a local file, skipped.")]);
        }

        var symbolsRoot = ResolveSymbolsRoot(source);
        if (symbolsRoot is null)
        {
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(
                [new ValidationIssue(ValidationSeverity.Warning, $"Could not resolve a 'symbols' folder next to '{source}'; icon check skipped.")]);
        }

        var issues = new List<ValidationIssue>();
        issues.Add(new ValidationIssue(ValidationSeverity.Info, $"Symbols root: {symbolsRoot}"));

        IReadOnlyList<IconReference> references;
        using (var content = File.OpenRead(source))
        {
            references = new IconReferenceSource(Country).ReadReferences(content);
        }

        var checkedPaths = new HashSet<string>(StringComparer.Ordinal);
        var missingPaths = new SortedSet<string>(StringComparer.Ordinal);
        var uncheckedTemplates = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var reference in references)
        {
            var paths = ExpandedPaths(reference);
            if (paths.Count == 0)
            {
                uncheckedTemplates.Add($"{reference.PathTemplate} ({reference.Origin})");
                continue;
            }

            foreach (var relativePath in paths)
            {
                if (!checkedPaths.Add(relativePath))
                {
                    continue;
                }

                var fullPath = Path.Combine(symbolsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar) + IconExtension);
                if (!File.Exists(fullPath))
                {
                    missingPaths.Add(relativePath);
                }
            }
        }

        issues.Add(new ValidationIssue(ValidationSeverity.Info, $"{checkedPaths.Count} distinct icon path(s) checked for {Country}."));
        foreach (var missing in missingPaths)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"Missing icon: symbols/{missing}{IconExtension}"));
        }

        foreach (var template in uncheckedTemplates)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Warning, $"Templated icon not checked (open pattern, no example): {template}"));
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    // The concrete relative paths a reference resolves to: itself for a
    // literal value. A templated value is checked through the example path its
    // case declares, plus one path per value a closed pattern accepts (see
    // TemplatedValueExpander). The map keeps the braces in the file name: value
    // 110 for "fr/signs/L/L{}" is the icon "fr/signs/L/L{110}". Empty when a
    // templated value has neither an example nor a closed pattern.
    private static List<string> ExpandedPaths(IconReference reference)
    {
        if (!reference.IsTemplated)
        {
            return [reference.PathTemplate];
        }

        var paths = new List<string>();
        if (reference.Example is not null)
        {
            paths.Add(reference.Example);
        }

        if (reference.ValuePattern is not null)
        {
            paths.AddRange(TemplatedValueExpander.Expand(reference.ValuePattern)
                .Select(value => reference.PathTemplate.Replace(IconReference.Placeholder, $"{{{value}}}", StringComparison.Ordinal)));
        }

        return paths;
    }

    // The YAML sits in a "features" folder; "symbols" is that folder's
    // sibling. Null when the path has no parent to derive a sibling from.
    // Internal (not private) so SvgOptimizationValidator can locate the same
    // "symbols/fr" folder for the optimization scan, without duplicating this
    // resolution logic.
    internal static string? ResolveSymbolsRoot(string yamlPath)
    {
        var featuresDir = Path.GetDirectoryName(Path.GetFullPath(yamlPath));
        var parentDir = featuresDir is null ? null : Path.GetDirectoryName(featuresDir);
        return parentDir is null ? null : Path.Combine(parentDir, SymbolsFolderName);
    }
}
