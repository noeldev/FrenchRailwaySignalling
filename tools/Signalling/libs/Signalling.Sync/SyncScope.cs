// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Sync;

// Controls how far the comparison reaches.
public enum SyncScope
{
    // Only keys the consumer discriminates on are compared; producer keys the
    // consumer ignores are out of scope. Used when the consumer legitimately
    // renders a subset, such as the ORM map.
    ConsumerKeys,

    // Every key on both sides is compared, so a producer key absent from the
    // consumer is reported. Used when the consumer is the authority, such as the
    // wiki specification.
    AllKeys,
}
