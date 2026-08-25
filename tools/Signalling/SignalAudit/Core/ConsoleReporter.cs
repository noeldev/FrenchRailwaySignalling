// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalAudit.Core;

// Renders validator progress and results to the console and returns error counts
// so the caller can build the process exit code. When a transcript writer is
// supplied, every line is also written to it without color codes, and each
// validator's issues are accumulated so the run can be exported as JSON.
public sealed class ConsoleReporter
{
    private readonly TextWriter? _transcript;
    private readonly List<ValidatorReport> _reports = [];
    private string _currentValidator = string.Empty;

    public ConsoleReporter(TextWriter? transcript = null)
    {
        _transcript = transcript;
    }

    public IReadOnlyList<ValidatorReport> Reports => _reports;

    // Announces a validator before it runs so long checks (network) show progress.
    public void BeginValidator(string validatorName)
    {
        _currentValidator = validatorName;
        Emit(string.Empty);
        Emit($"=== {validatorName} ===");
    }

    // Reports one validator's issues and returns the number of errors among them.
    public int Report(IReadOnlyList<ValidationIssue> issues)
    {
        _reports.Add(new ValidatorReport(_currentValidator, issues));

        if (issues.Count == 0)
        {
            Emit(ValidationSeverity.Info, "No issue found.");
            return 0;
        }

        var errors = 0;
        var warnings = 0;
        foreach (var issue in issues)
        {
            Emit(issue.Severity, Format(issue));
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

        Emit(
            errors > 0 ? ValidationSeverity.Error : ValidationSeverity.Warning,
            $"{errors} error(s), {warnings} warning(s).");

        return errors;
    }

    public void ReportSummary(int errorCount)
    {
        Emit(string.Empty);
        if (errorCount == 0)
        {
            Emit(ValidationSeverity.Info, "Validation passed with no error.");
        }
        else
        {
            Emit(ValidationSeverity.Error, $"Validation failed with {errorCount} error(s).");
        }
    }

    private static string Format(ValidationIssue issue) =>
        issue.HasPosition
            ? $"[{issue.Severity}] Line {issue.Line}, Col {issue.Column}: {issue.Message}"
            : $"[{issue.Severity}] {issue.Message}";

    // Writes an uncolored line to the console and the transcript.
    private void Emit(string text)
    {
        Console.WriteLine(text);
        _transcript?.WriteLine(text);
    }

    // Writes a colored line to the console and the same line, without color, to
    // the transcript.
    private void Emit(ValidationSeverity severity, string text)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = severity switch
        {
            ValidationSeverity.Error => ConsoleColor.Red,
            ValidationSeverity.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Gray,
        };
        Console.WriteLine(text);
        Console.ForegroundColor = previous;

        _transcript?.WriteLine(text);
    }
}
