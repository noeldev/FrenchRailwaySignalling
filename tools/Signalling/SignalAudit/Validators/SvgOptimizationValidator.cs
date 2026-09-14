// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Verifies that every SVG icon under the preset icon root, and under the
// ORM-vector map's FR symbols folder when a local YAML source is available,
// has been exported in Inkscape's compact "Optimized SVG" format rather than
// the default "Inkscape SVG" format. The default format keeps editor state
// (Inkscape and Sodipodi namespaces, a metadata block, XML comments) that
// bloats the file and serves no purpose once the icon ships. Scanning both
// folders directly, instead of following preset/YAML references, also catches
// icons that sit on disk but are not currently referenced by either.
public sealed class SvgOptimizationValidator : IValidator
{
    private static readonly XNamespace InkscapeNamespace = "http://www.inkscape.org/namespaces/inkscape";
    private static readonly XNamespace SodipodiNamespace = "http://sodipodi.sourceforge.net/DTD/sodipodi-0.0.dtd";
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
    // just because nothing currently references it. Issues report the path
    // relative to root for readability.
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

            var reasons = FindNonOptimizedReasons(fullPath);
            if (reasons.Count == 0)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                $"Icon '{relativePath}' is not an optimized SVG ({string.Join(", ", reasons)}). " +
                "Re-export it with Inkscape's 'Optimized SVG' format."));
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

    // Loads the SVG and looks for the editor leftovers that a normal Inkscape
    // save keeps and the "Optimized SVG" export strips out.
    private static List<string> FindNonOptimizedReasons(string fullPath)
    {
        var reasons = new List<string>();

        XDocument svg;
        try
        {
            svg = XDocument.Load(fullPath, LoadOptions.None);
        }
        catch (Exception)
        {
            // Well-formedness is not this validator's concern; a file that
            // fails to parse cannot be checked any further here.
            return reasons;
        }

        if (svg.Root is null)
        {
            return reasons;
        }

        if (HasNamespaceUsage(svg, InkscapeNamespace))
        {
            reasons.Add("inkscape namespace present");
        }

        if (HasNamespaceUsage(svg, SodipodiNamespace))
        {
            reasons.Add("sodipodi namespace present");
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

    // A namespace can appear either as an element (sodipodi:namedview) or as an
    // attribute (inkscape:label="..."); either one means the file went through
    // a normal Inkscape save rather than the optimized export.
    private static bool HasNamespaceUsage(XDocument document, XNamespace ns)
    {
        return document.Descendants().Any(e => e.Name.Namespace == ns)
            || document.Descendants().Attributes().Any(a => a.Name.Namespace == ns);
    }
}
