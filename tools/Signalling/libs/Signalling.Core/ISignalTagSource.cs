// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using Signalling.Core.Model;

namespace Signalling.Core;

// A reader that turns one signalling source (JOSM preset, ORM-vector YAML, ...)
// into a uniform set of tag rules. Readers are transport agnostic: they parse a
// stream that an ISourceResolver has already opened, so the same reader serves a
// local file and a remote URL without change.
public interface ISignalTagSource
{
    // Short label shown in the report, for example "JOSM preset".
    string FormatName { get; }

    // Country namespace the rules belong to, for example "FR".
    string Country { get; }

    // Parses the already-opened content into tag rules.
    IReadOnlyList<SignalTagRule> ReadRules(Stream content);
}
