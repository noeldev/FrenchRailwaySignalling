// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml;
using System.Xml.Linq;

namespace PresetValidator.Core;

// Turns an element position into a ValidationIssue, keeping the line lookup in
// one place instead of repeating the cast in every validator.
public static class XmlLineInfoExtensions
{
    public static ValidationIssue ToIssue(this XElement element, ValidationSeverity severity, string message)
    {
        var lineInfo = (IXmlLineInfo)element;
        return lineInfo.HasLineInfo()
            ? new ValidationIssue(severity, message, lineInfo.LineNumber, lineInfo.LinePosition)
            : new ValidationIssue(severity, message);
    }
}
