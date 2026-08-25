// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalAudit.Core;

// Parses and holds the command line configuration, merged with the Sources
// section of configuration. Parse returns null when the arguments are invalid,
// when help was requested, or when no preset was given, so Program can exit
// early after printing usage.
//
// The preset is the required first positional argument. The cross-source audits
// are opt-in through --yaml and --wiki; without them the offline preset checks
// still run. For each of those flags: no value uses the configured local source,
// the value "online" uses the configured URL, and any other value is used as an
// explicit path or URL.
public sealed class CommandLineOptions
{
    private const int DefaultTimeoutSeconds = 15;
    private const string OnlineKeyword = "online";

    public required string XmlPath { get; init; }

    // Null means the schema location is resolved from the preset file and
    // downloaded automatically. A value forces a specific local file or URL.
    public string? XsdPath { get; init; }

    public required string IconRoot { get; init; }

    // Resolved ORM-vector YAML source, or null when the map audit is not enabled.
    public string? OrmVectorYamlSource { get; init; }

    // True when --wiki was given: enables the wiki link checks.
    public bool WikiRequested { get; init; }

    // Resolved wiki content source, or null when only link checks are wanted
    // (bare --wiki with no local cache configured).
    public string? WikiSyncSource { get; init; }

    public string? LogPath { get; init; }

    public string? JsonReportPath { get; init; }

    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    public static CommandLineOptions? Parse(string[] args, SourcesSettings sources)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return null;
        }

        string? xmlPath = null;
        string? xsdPath = null;
        string? iconRoot = null;
        var yamlRequested = false;
        string? yamlValue = null;
        var wikiRequested = false;
        string? wikiValue = null;
        string? logPath = null;
        string? jsonReportPath = null;
        var timeout = DefaultTimeoutSeconds;

        // The preset is the first positional argument; taking it before the loop
        // keeps an option with an optional value from swallowing it.
        var start = 0;
        if (!args[0].StartsWith('-'))
        {
            xmlPath = args[0];
            start = 1;
        }

        for (var i = start; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage();
                    return null;

                case "--xsd":
                {
                    if (!TryReadValue(args, ref i, out var value)) return null;
                    xsdPath = value;
                    break;
                }

                case "--icon-root":
                {
                    if (!TryReadValue(args, ref i, out var value)) return null;
                    iconRoot = value;
                    break;
                }

                case "--yaml":
                    yamlRequested = true;
                    yamlValue = ReadOptionalValue(args, ref i);
                    break;

                case "--wiki":
                    wikiRequested = true;
                    wikiValue = ReadOptionalValue(args, ref i);
                    break;

                case "--log":
                {
                    if (!TryReadValue(args, ref i, out var value)) return null;
                    logPath = value;
                    break;
                }

                case "--report-json":
                {
                    if (!TryReadValue(args, ref i, out var value)) return null;
                    jsonReportPath = value;
                    break;
                }

                case "--timeout":
                {
                    if (!TryReadValue(args, ref i, out var value)) return null;
                    if (!int.TryParse(value, out timeout) || timeout <= 0)
                    {
                        Console.Error.WriteLine($"Invalid value for --timeout: {value}");
                        return null;
                    }

                    break;
                }

                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    PrintUsage();
                    return null;
            }
        }

        if (string.IsNullOrWhiteSpace(xmlPath))
        {
            Console.Error.WriteLine("No preset file given.");
            PrintUsage();
            return null;
        }

        var yamlSource = ResolveSource(yamlRequested, yamlValue, sources.OrmVectorYaml);
        if (yamlRequested && string.IsNullOrWhiteSpace(yamlSource))
        {
            Console.Error.WriteLine(
                "--yaml has no source: set Sources:OrmVectorYaml:Local, or use --yaml online, or --yaml <path|url>.");
            return null;
        }

        // A bare --wiki with no configured local cache still runs the link
        // checks; only the content sync is skipped, so this is not an error.
        var wikiSource = ResolveSource(wikiRequested, wikiValue, sources.Wiki);

        // Icon paths inside a preset are relative to the preset file, so the
        // preset directory is the natural default root for icon resolution.
        iconRoot ??= Path.GetDirectoryName(Path.GetFullPath(xmlPath)) ?? Directory.GetCurrentDirectory();

        return new CommandLineOptions
        {
            XmlPath = xmlPath,
            XsdPath = xsdPath,
            IconRoot = iconRoot,
            OrmVectorYamlSource = string.IsNullOrWhiteSpace(yamlSource) ? null : yamlSource,
            WikiRequested = wikiRequested,
            WikiSyncSource = string.IsNullOrWhiteSpace(wikiSource) ? null : wikiSource,
            LogPath = logPath,
            JsonReportPath = jsonReportPath,
            TimeoutSeconds = timeout,
        };
    }

    // Resolves a --yaml / --wiki source: no value uses the configured local
    // path, "online" uses the configured URL, anything else is explicit.
    private static string? ResolveSource(bool requested, string? value, SourceLocation configured)
    {
        if (!requested)
        {
            return null;
        }

        if (value is null)
        {
            return NullIfEmpty(configured.Local);
        }

        return string.Equals(value, OnlineKeyword, StringComparison.OrdinalIgnoreCase)
            ? NullIfEmpty(configured.Url)
            : value;
    }

    // Reads the value that follows an option only when it is present and is not
    // itself another option, so a bare flag keeps its default meaning.
    private static string? ReadOptionalValue(string[] args, ref int index)
    {
        if (index + 1 < args.Length && !args[index + 1].StartsWith('-'))
        {
            return args[++index];
        }

        return null;
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            Console.Error.WriteLine($"Missing value after {args[index]}");
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            JOSM preset validator and cross-source consistency audit (ORM map, OSM wiki).

            Usage:
              SignalAudit <preset.xml> [options]

            The offline preset checks (schema, chunks, icons, internal links,
            structure, menu paths, match expressions) always run. --yaml and --wiki
            add the network cross-source audits.

            Arguments:
              <preset.xml>            Preset file to validate (absolute or relative path).

            Options:
              --xsd <path|url>        Schema override. When omitted, the schema
                                      location is read from the preset file and
                                      downloaded automatically.
              --icon-root <dir>       Base directory for icon resolution
                                      (default: the preset file directory).
              --yaml [source]         Audit the preset against the ORM-vector map YAML.
                                      No value: configured local file. 'online':
                                      configured URL. Or an explicit path/url.
              --wiki [source]         Audit the preset against the OSM wiki: link and
                                      anchor integrity, plus tag coverage. No value:
                                      configured local cache. 'online': configured URL
                                      (MediaWiki API). Or an explicit path/url.
              --log <path>            Write a plain-text transcript of the run.
              --report-json <path>    Write a structured JSON report of all issues.
              --timeout <sec>         HTTP timeout for downloads and checks (default: 15).
              -h, --help              Show this help.
            """);
    }
}
