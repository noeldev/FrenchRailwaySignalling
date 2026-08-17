// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace PresetValidator.Core;

// Renders validator progress and results to the console and returns error
// counts so the caller can build the process exit code.
public sealed class ConsoleReporter
{
    // Announces a validator before it runs so long checks (network) show progress.
    public void BeginValidator(string validatorName)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {validatorName} ===");
    }

    // Returns the number of errors contained in the reported issues.
    public int Report(IReadOnlyList<ValidationIssue> issues)
    {
        if (issues.Count == 0)
        {
            WriteLine(ValidationSeverity.Info, "No issue found.");
            return 0;
        }

        var errors = 0;
        var warnings = 0;
        foreach (var issue in issues)
        {
            WriteLine(issue.Severity, Format(issue));
            switch (issue.Severity)
            {
                case ValidationSeverity.Error:
                    errors++;
                    break;
                case ValidationSeverity.Warning:
                    warnings++;
                    break;
            }
        }

        WriteLine(
            errors > 0 ? ValidationSeverity.Error : ValidationSeverity.Warning,
            $"{errors} error(s), {warnings} warning(s).");

        return errors;
    }

    public void ReportSummary(int errorCount)
    {
        Console.WriteLine();
        if (errorCount == 0)
        {
            WriteLine(ValidationSeverity.Info, "Validation passed with no error.");
        }
        else
        {
            WriteLine(ValidationSeverity.Error, $"Validation failed with {errorCount} error(s).");
        }
    }

    private static string Format(ValidationIssue issue) =>
        issue.HasPosition
            ? $"[{issue.Severity}] Line {issue.Line}, Col {issue.Column}: {issue.Message}"
            : $"[{issue.Severity}] {issue.Message}";

    private static void WriteLine(ValidationSeverity severity, string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = severity switch
        {
            ValidationSeverity.Error => ConsoleColor.Red,
            ValidationSeverity.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray
        };
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
