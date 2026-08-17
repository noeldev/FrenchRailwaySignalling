// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;

namespace PresetValidator.Core;

// Shared, read-only state handed to every validator. The XML document is loaded
// once with line information so issues can point back to the source file.
public sealed class ValidationContext(CommandLineOptions options, XDocument document)
{
    public CommandLineOptions Options { get; } = options;

    public XDocument Document { get; } = document;
}
