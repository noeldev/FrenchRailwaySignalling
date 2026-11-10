// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;
using RailwayMapBot.Osm;

namespace RailwayMapBot.Planning;

// Honours the opt-out requests in full: a mapper who asked to be left alone
// keeps every node they ever edited, not only those they edited last. The
// history of a node is read only when someone opted out, and only for the
// nodes about to be edited.
public sealed class ContributorGuard(OsmApiClient api, OptOutSettings optOut)
{
    private readonly HashSet<string> _users = new(optOut.Users, StringComparer.Ordinal);

    public async Task<IReadOnlyList<EditDecision>> ApplyAsync(IReadOnlyList<EditDecision> decisions, CancellationToken cancellationToken)
    {
        if (_users.Count == 0)
        {
            return decisions;
        }

        List<EditDecision> guarded = [];
        foreach (var decision in decisions)
        {
            var optedOut = decision.IsEdit && (await api.GetNodeContributorsAsync(decision.Node.Id, cancellationToken)).Overlaps(_users);
            guarded.Add(optedOut ? EditDecision.Skipped(decision.Node, SkipReason.OptedOut, decision.Corrections) : decision);
        }

        return guarded;
    }
}
