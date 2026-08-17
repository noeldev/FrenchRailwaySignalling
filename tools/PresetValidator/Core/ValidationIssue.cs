// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace PresetValidator.Core;

// Severity levels reported by the validators, ordered from least to most critical.
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

// Single problem discovered by a validator. Line and column are optional because
// some checks (file system, network) have no XML position to report.
public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message,
    int Line = 0,
    int Column = 0)
{
    public bool HasPosition => Line > 0;
}
