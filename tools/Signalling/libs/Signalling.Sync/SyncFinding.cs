// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Sync;

// The kinds of mismatch the comparator reports, in neutral producer/consumer
// terms. Severity and wording are left to the caller, which knows what the two
// sides represent (map, wiki, ...).
public enum SyncFindingKind
{
    // A value the producer can emit on a shared key that no consumer rule accepts.
    ProducerValueUnmatched,

    // A key the producer uses that the consumer does not have at all. Only
    // reported in AllKeys scope.
    ProducerKeyUnmatched,

    // The producer emits an open value domain on a shared key that the consumer
    // covers only with specific values (no catch-all or pattern).
    ProducerOpenDomainPartial,

    // A value the consumer matches that no producer emits.
    ConsumerValueUnmatched,

    // A key the consumer discriminates on that no producer emits.
    ConsumerKeyUnmatched,
}

// A single mismatch. Value is null for key-level and open-domain findings.
// Origin points back to the producing preset item or consumer feature/section.
public sealed record SyncFinding(SyncFindingKind Kind, string Key, string? Value, string Origin);
