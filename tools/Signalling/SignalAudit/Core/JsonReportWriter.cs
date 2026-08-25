// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SignalAudit.Core;

// Writes the accumulated validator reports as an indented JSON file, so a run
// can be analyzed or diffed instead of scraped from the console transcript.
public static class JsonReportWriter
{
    public static void Write(string path, IReadOnlyList<ValidatorReport> reports)
    {
        var model = reports.Select(report => new
        {
            validator = report.Validator,
            issues = report.Issues.Select(issue => new
            {
                severity = issue.Severity.ToString(),
                line = issue.HasPosition ? issue.Line : (int?)null,
                column = issue.HasPosition ? issue.Column : (int?)null,
                message = issue.Message,
            }),
        });

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(model, options), new UTF8Encoding(false));
    }
}
