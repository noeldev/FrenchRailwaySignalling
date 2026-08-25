// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalAudit.Core;

// One validator's name paired with the issues it produced, accumulated by the
// reporter so the run can also be written as a structured JSON report.
public sealed record ValidatorReport(string Validator, IReadOnlyList<ValidationIssue> Issues);
