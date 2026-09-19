// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Verifies that every SVG icon under the preset icon root, and under the
// ORM-vector map's FR symbols folder when a local YAML source is available,
// is ready to publish:
// - exported in Inkscape's compact "Optimized SVG" format rather than the
//   default "Inkscape SVG" format, which keeps editor state (Inkscape
//   namespace, a metadata block, XML comments) that bloats the file and
//   serves no purpose once the icon ships. The Sodipodi namespace is not
//   checked on its own: a normal Inkscape save never emits it without the
//   Inkscape namespace alongside it, so the Inkscape check already covers it.
// - free of <text> elements: unconverted text renders using whatever font is
//   installed on the machine doing the rendering, which is rarely the exact
//   font the icon was designed with. Glyphs must be converted to paths
//   before export (Inkscape: Path > Object to Path).
// Scanning both folders directly, instead of following preset/YAML
// references, also catches icons that sit on disk but are not currently
// referenced by either.
public sealed class SvgOptimizationValidator : IValidator
{
    private static readonly XNamespace InkscapeNamespace = "http://www.inkscape.org/namespaces/inkscape";
    private const string FrSymbolsSubfolder = "fr";

    public string Name => "SVG optimization";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var checkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CheckFolder(context.Options.IconRoot, checkedFiles, issues, cancellationToken);

        var symbolsRoot = ResolveFrSymbolsRoot(context.Options.OrmVectorYamlSource);
        if (symbolsRoot is not null)
        {
            CheckFolder(symbolsRoot, checkedFiles, issues, cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    // Recursively checks every .svg file under root, so no icon is missed
    // just because nothing currently references it.
    private static void CheckFolder(
        string root,
        HashSet<string> checkedFiles,
        List<ValidationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(root, "*.svg", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(filePath);
            if (!checkedFiles.Add(fullPath))
            {
                continue;
            }

            CheckFile(root, fullPath, issues);
        }
    }

    // The two checks catch unrelated problems with unrelated fixes (re-export
    // vs convert text to paths), so each gets its own issue instead of being
    // merged into one combined message - a single message listing multiple
    // unrelated reasons made it unclear which fix actually applied.
    private static void CheckFile(string root, string fullPath, List<ValidationIssue> issues)
    {
        XDocument svg;
        try
        {
            svg = XDocument.Load(fullPath, LoadOptions.None);
        }
        catch (Exception)
        {
            // Well-formedness is not this validator's concern; a file that
            // fails to parse cannot be checked any further here.
            return;
        }

        if (svg.Root is null)
        {
            return;
        }

        var relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');

        var exportReasons = FindNonOptimizedExportReasons(svg);
        if (exportReasons.Count > 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                $"Icon '{relativePath}': not exported as Optimized SVG ({string.Join(", ", exportReasons)})."));
        }

        if (svg.Descendants().Any(e => e.Name.LocalName == "text"))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                $"Icon '{relativePath}': contains <text> elements, not converted to paths."));
        }
    }

    // The FR symbols folder is the ORM-vector map's "symbols/fr" folder, the
    // same sibling-of-"features" resolution MapIconValidator uses. Returns
    // null when there is no local YAML source to derive it from (missing, or
    // a URL, matching MapIconValidator's own skip condition).
    private static string? ResolveFrSymbolsRoot(string? ormVectorYamlSource)
    {
        if (string.IsNullOrWhiteSpace(ormVectorYamlSource))
        {
            return null;
        }

        if (Uri.TryCreate(ormVectorYamlSource, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return null;
        }

        var symbolsRoot = MapIconValidator.ResolveSymbolsRoot(ormVectorYamlSource);
        return symbolsRoot is null ? null : Path.Combine(symbolsRoot, FrSymbolsSubfolder);
    }

    // Editor leftovers that a normal Inkscape save keeps and the "Optimized
    // SVG" export strips out.
    private static List<string> FindNonOptimizedExportReasons(XDocument svg)
    {
        List<string> reasons = [];

        if (HasNamespaceUsage(svg, InkscapeNamespace))
        {
            reasons.Add("Inkscape namespace present");
        }

        if (svg.Descendants().Any(e => e.Name.LocalName == "metadata"))
        {
            reasons.Add("metadata element present");
        }

        if (svg.DescendantNodes().OfType<XComment>().Any())
        {
            reasons.Add("XML comments present");
        }

        return reasons;
    }

    // The namespace can appear either as an element (inkscape:* elements like
    // sodipodi:namedview's inkscape:* attributes) or as an attribute
    // (inkscape:label="..." on a layer); either one means the file went
    // through a normal Inkscape save rather than the optimized export.
    private static bool HasNamespaceUsage(XDocument document, XNamespace ns)
    {
        return document.Descendants().Any(e => e.Name.Namespace == ns)
            || document.Descendants().Attributes().Any(a => a.Name.Namespace == ns);
    }
}
