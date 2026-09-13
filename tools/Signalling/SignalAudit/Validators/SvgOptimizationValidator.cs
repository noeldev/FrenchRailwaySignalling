// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Verifies that every referenced local SVG icon has been exported in
// Inkscape's compact "Optimized SVG" format rather than the default
// "Inkscape SVG" format. The default format keeps editor state (Inkscape and
// Sodipodi namespaces, a metadata block, XML comments) that bloats the file
// and serves no purpose once the icon ships in a preset.
public sealed class SvgOptimizationValidator : IValidator
{
    private static readonly XNamespace InkscapeNamespace = "http://www.inkscape.org/namespaces/inkscape";
    private static readonly XNamespace SodipodiNamespace = "http://sodipodi.sourceforge.net/DTD/sodipodi-0.0.dtd";

    public string Name => "SVG optimization";

    public Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var iconRoot = context.Options.IconRoot;

        // A single icon can be referenced many times; check each distinct path once.
        var checkedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in context.Document.Descendants().Where(HasLocalSvgIcon))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var iconPath = element.Attribute("icon")!.Value;
            if (!checkedPaths.Add(iconPath))
            {
                continue;
            }

            var resolution = IconPathResolver.Resolve(iconRoot, iconPath);
            if (resolution.FullPath is null)
            {
                // Missing files are already reported by IconValidator.
                continue;
            }

            var reasons = FindNonOptimizedReasons(resolution.FullPath);
            if (reasons.Count > 0)
            {
                issues.Add(element.ToIssue(
                    ValidationSeverity.Warning,
                    $"Icon '{iconPath}' is not an optimized SVG ({string.Join(", ", reasons)}). " +
                    "Re-export it with Inkscape's 'Optimized SVG' format."));
            }
        }

        return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
    }

    private static bool HasLocalSvgIcon(XElement element)
    {
        var icon = element.Attribute("icon")?.Value;
        return !string.IsNullOrWhiteSpace(icon)
            && !IsRemote(icon)
            && icon.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRemote(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

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
