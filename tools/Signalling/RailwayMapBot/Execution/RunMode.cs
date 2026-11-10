// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Execution;

public enum RunMode
{
    // Decide from the listed nodes as they are; no server is read.
    Offline,

    // Read the nodes again from the server and decide; write nothing.
    DryRun,

    // Read the nodes again, decide and upload.
    Write,
}
