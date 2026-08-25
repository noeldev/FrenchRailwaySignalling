// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Sync;

namespace SignalAudit.Core;

// Turns neutral sync findings into validation issues, worded for a given
// consumer (the map, the wiki). An unmatched producer value or key is an error
// (the preset asserts something the consumer does not have); everything else is
// a warning.
public static class SyncIssueFormatter
{
    public static ValidationIssue ToIssue(SyncFinding finding, string consumerName)
    {
        var severity = finding.Kind is SyncFindingKind.ProducerValueUnmatched or SyncFindingKind.ProducerKeyUnmatched
            ? ValidationSeverity.Error
            : ValidationSeverity.Warning;

        var message = finding.Kind switch
        {
            SyncFindingKind.ProducerValueUnmatched =>
                $"Preset value '{finding.Key}={finding.Value}' is not covered by {consumerName}.",
            SyncFindingKind.ProducerKeyUnmatched =>
                $"Preset key '{finding.Key}' is not covered by {consumerName}.",
            SyncFindingKind.ProducerOpenDomainPartial =>
                $"Preset emits an open value domain on '{finding.Key}', only partially covered by {consumerName}.",
            SyncFindingKind.ConsumerValueUnmatched =>
                $"No preset emits '{finding.Key}={finding.Value}', referenced by {consumerName}.",
            SyncFindingKind.ConsumerKeyUnmatched =>
                $"No preset emits key '{finding.Key}', referenced by {consumerName}.",
            _ => finding.Key,
        };

        return new ValidationIssue(severity, $"{message} (origin: {finding.Origin})");
    }
}
