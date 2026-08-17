// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace PresetValidator.Core;

// Parses and holds the command line configuration. Parse returns null when the
// arguments are invalid or when help was requested, so Program can exit early.
public sealed class CommandLineOptions
{
    private const string DefaultXmlPath = "presets.xml";
    private const int DefaultTimeoutSeconds = 15;

    public required string XmlPath { get; init; }

    // Null means the schema location is resolved from the preset file and
    // downloaded automatically. A value forces a specific local file or URL.
    public string? XsdPath { get; init; }

    public required string IconRoot { get; init; }

    public bool CheckWiki { get; init; }

    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

    public static CommandLineOptions? Parse(string[] args)
    {
        var xmlPath = DefaultXmlPath;
        string? xsdPath = null;
        string? iconRoot = null;
        var checkWiki = false;
        var timeout = DefaultTimeoutSeconds;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    PrintUsage();
                    return null;

                case "--xml":
                {
                    if (!TryReadValue(args, ref i, out var value)) return null;
                    xmlPath = value;
                    break;
                }

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

                case "--check-wiki":
                    checkWiki = true;
                    break;

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
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"Unknown option: {arg}");
                        PrintUsage();
                        return null;
                    }

                    // The first bare argument is treated as the preset file path.
                    xmlPath = arg;
                    break;
            }
        }

        // Icon paths inside a preset are relative to the preset file, so the
        // preset directory is the natural default root for icon resolution.
        iconRoot ??= Path.GetDirectoryName(Path.GetFullPath(xmlPath)) ?? Directory.GetCurrentDirectory();

        return new CommandLineOptions
        {
            XmlPath = xmlPath,
            XsdPath = xsdPath,
            IconRoot = iconRoot,
            CheckWiki = checkWiki,
            TimeoutSeconds = timeout
        };
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

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            JOSM tagging preset validator.

            Usage:
              PresetValidator [options] [<presets.xml>]

            Options:
              --xml <path>            Preset file to validate (default: presets.xml).
              --xsd <path|url>        Schema override. When omitted, the schema
                                      location is read from the preset file
                                      (xsi:schemaLocation or the default namespace)
                                      and downloaded automatically.
              --icon-root <dir>       Base directory for icon resolution
                                      (default: the preset file directory).
              --check-wiki            Enable network checks of wiki links and anchors.
              --timeout <sec>         HTTP timeout for schema download and wiki checks (default: 15).
              -h, --help              Show this help.
            """);
    }
}
