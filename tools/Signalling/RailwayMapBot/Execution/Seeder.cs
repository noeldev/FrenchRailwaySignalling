// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;
using RailwayMapBot.Osm;
using RailwayMapBot.Planning;

namespace RailwayMapBot.Execution;

// Creates test nodes on a sandbox server, copied from signals the bot would
// fix, so a real run can be rehearsed there: the sandbox has its own database,
// without the French signals. Never used on a production server.
public sealed class Seeder(OsmApiClient api, ChangesetTagBuilder tagBuilder, ChangesetSettings changeset)
{
    // The nodes to copy: first one node per correction, to cover as many
    // rules as possible, then any other node to fix.
    public static IReadOnlyList<OsmNode> Select(IEnumerable<EditDecision> decisions, int count)
    {
        var edits = decisions.Where(decision => decision.IsEdit).ToList();
        HashSet<string> covered = new(StringComparer.Ordinal);

        var picked = edits.Where(edit => edit.Corrections.Count(covered.Add) > 0).Take(count).ToList();
        picked.AddRange(edits.Except(picked).Take(count - picked.Count));
        return [.. picked.Select(edit => edit.Node)];
    }

    // Returns the ids of the created nodes.
    public async Task<IReadOnlyList<long>> CreateAsync(IReadOnlyList<OsmNode> nodes, CancellationToken cancellationToken)
    {
        var changesetId = await api.CreateChangesetAsync(tagBuilder.Build(changeset.SeedComment, string.Empty, 1), cancellationToken);
        try
        {
            // New nodes are given negative placeholder ids.
            var writes = nodes.Select((node, index) => new NodeWrite(-(index + 1), 0, node.Lat, node.Lon, node.Tags));
            var results = await api.UploadAsync(changesetId, OsmXml.WriteChange(changesetId, writes, create: true, Banner.ProductVersion()), CancellationToken.None);
            return [.. results.Values.Select(result => result.NewId)];
        }
        finally
        {
            await api.CloseChangesetAsync(changesetId, CancellationToken.None);
        }
    }
}
