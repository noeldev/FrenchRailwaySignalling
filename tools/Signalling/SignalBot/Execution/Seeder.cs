// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Osm;
using SignalBot.Planning;

namespace SignalBot.Execution;

// Creates test nodes on a sandbox server, copied from signals the bot would
// fix, so a real run can be rehearsed there: the sandbox has its own database,
// without the French signals. Never used on a production server.
// The copies keep the tags, not the positions: they are laid out on a small
// grid starting at the first node, since the server refuses a changeset
// spread over a whole region from a new account.
public sealed class Seeder(OsmApiClient api, ChangesetTagBuilder tagBuilder, ChangesetSettings changeset)
{
    // Test nodes created when no number is given.
    public const int DefaultCount = 50;

    // About 50 m between two test nodes.
    private const double GridStepDegrees = 0.0005;

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
            var columns = (int)Math.Ceiling(Math.Sqrt(nodes.Count));
            var origin = nodes[0];
            var writes = nodes.Select((node, index) => new NodeWrite(
                -(index + 1),
                0,
                origin.Lat + (index / columns * GridStepDegrees),
                origin.Lon + (index % columns * GridStepDegrees),
                node.Tags));
            var results = await api.UploadAsync(changesetId, OsmXml.WriteChange(changesetId, writes, create: true, Banner.ProductVersion()), CancellationToken.None);
            return [.. results.Values.Select(result => result.NewId)];
        }
        finally
        {
            await api.CloseChangesetAsync(changesetId, CancellationToken.None);
        }
    }
}
