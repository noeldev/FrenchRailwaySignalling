// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using PresetValidator.Core;

namespace PresetValidator.Validators;

// Validates the preset file against the JOSM tagging preset schema. The schema
// location is taken from the preset file itself (an xsi:schemaLocation hint or,
// as a fallback, the default namespace turned into an .xsd URL) and downloaded
// on the fly. A local file or URL can still be forced with --xsd. When the
// schema cannot be resolved or fetched the check is skipped with a warning
// rather than failing the whole run.
public sealed class XsdSchemaValidator : IValidator
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public string Name => "XSD schema";

    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();

        var source = ResolveSource(context);
        if (source.Kind == SchemaSourceKind.None)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "Unable to determine a schema location from the preset file; step skipped."));
            return issues;
        }

        XmlSchema? schema;
        try
        {
            schema = source.Kind == SchemaSourceKind.Remote
                ? await DownloadSchemaAsync(source.Location, context.Options.TimeoutSeconds, cancellationToken)
                : ReadLocalSchema(source.Location);
        }
        catch (Exception ex)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                $"Schema could not be loaded ({ex.Message}); step skipped: {source.Location}"));
            return issues;
        }

        if (schema is null)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                $"Schema could not be parsed; step skipped: {source.Location}"));
            return issues;
        }

        issues.Add(new ValidationIssue(ValidationSeverity.Info, $"Schema: {source.Location}"));

        var schemas = new XmlSchemaSet();
        schemas.Add(schema);

        context.Document.Validate(schemas, (_, e) =>
        {
            var severity = e.Severity == XmlSeverityType.Error
                ? ValidationSeverity.Error
                : ValidationSeverity.Warning;

            issues.Add(new ValidationIssue(
                severity,
                e.Message,
                e.Exception?.LineNumber ?? 0,
                e.Exception?.LinePosition ?? 0));
        });

        return issues;
    }

    // Decides where the schema comes from: an explicit --xsd override (resolved
    // against the current directory) or, by default, the preset file itself.
    private static SchemaSource ResolveSource(ValidationContext context)
    {
        var xsdOverride = context.Options.XsdPath;
        if (!string.IsNullOrWhiteSpace(xsdOverride))
        {
            return Classify(xsdOverride, Directory.GetCurrentDirectory());
        }

        var presetDirectory = Path.GetDirectoryName(Path.GetFullPath(context.Options.XmlPath))
            ?? Directory.GetCurrentDirectory();

        return ResolveFromDocument(context.Document, presetDirectory);
    }

    // Reads the schema location declared in the document, falling back to the
    // default namespace URI with an .xsd suffix (the JOSM convention).
    private static SchemaSource ResolveFromDocument(XDocument document, string presetDirectory)
    {
        var root = document.Root;
        if (root is null)
        {
            return SchemaSource.None;
        }

        var defaultNamespace = root.Name.NamespaceName;

        var declared = ReadDeclaredLocation(root, defaultNamespace);
        if (declared is not null)
        {
            return Classify(declared, presetDirectory);
        }

        if (Uri.TryCreate(defaultNamespace, UriKind.Absolute, out var namespaceUri)
            && (namespaceUri.Scheme == Uri.UriSchemeHttp || namespaceUri.Scheme == Uri.UriSchemeHttps))
        {
            var builder = new UriBuilder(namespaceUri) { Scheme = Uri.UriSchemeHttps, Port = -1 };
            if (!builder.Path.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
            {
                builder.Path += ".xsd";
            }

            return SchemaSource.Remote(builder.Uri.ToString());
        }

        return SchemaSource.None;
    }

    // xsi:noNamespaceSchemaLocation is a single URL. xsi:schemaLocation is a
    // whitespace separated list of (namespace, location) pairs; the location for
    // the document namespace is preferred, otherwise the first one is used.
    private static string? ReadDeclaredLocation(XElement root, string defaultNamespace)
    {
        var noNamespace = root.Attribute(Xsi + "noNamespaceSchemaLocation")?.Value;
        if (!string.IsNullOrWhiteSpace(noNamespace))
        {
            return noNamespace.Trim();
        }

        var schemaLocation = root.Attribute(Xsi + "schemaLocation")?.Value;
        if (string.IsNullOrWhiteSpace(schemaLocation))
        {
            return null;
        }

        var tokens = schemaLocation.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            if (string.Equals(tokens[i], defaultNamespace, StringComparison.Ordinal))
            {
                return tokens[i + 1];
            }
        }

        return tokens.Length >= 2 ? tokens[1] : null;
    }

    // A location is remote when it is an absolute http(s) URI; anything else is
    // treated as a file path relative to the given base directory.
    private static SchemaSource Classify(string location, string baseDirectory)
    {
        if (Uri.TryCreate(location, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return SchemaSource.Remote(location);
        }

        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, location));
        return SchemaSource.Local(fullPath);
    }

    private static XmlSchema? ReadLocalSchema(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Schema file not found.", path);
        }

        using var reader = XmlReader.Create(path);
        return XmlSchema.Read(reader, null);
    }

    private static async Task<XmlSchema?> DownloadSchemaAsync(string url, int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var client = HttpClientFactory.Create(timeoutSeconds);
        await using var stream = await client.GetStreamAsync(url, cancellationToken);
        return XmlSchema.Read(stream, null);
    }

    private enum SchemaSourceKind
    {
        None,
        Local,
        Remote
    }

    private readonly record struct SchemaSource(SchemaSourceKind Kind, string Location)
    {
        public static readonly SchemaSource None = new(SchemaSourceKind.None, string.Empty);

        public static SchemaSource Local(string path) => new(SchemaSourceKind.Local, path);

        public static SchemaSource Remote(string url) => new(SchemaSourceKind.Remote, url);
    }
}
