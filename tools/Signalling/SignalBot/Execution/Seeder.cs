// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Osm;
using SignalBot.Planning;

namespace SignalBot.Execution;

// Creates test nodes on a sandbox server, copied from signals the bot would
// fix, so a real run can be rehearsed there: the sandbox has its own database,
// without the French signals. Never used on a production server.
// The copies keep the tags and the positions of the real signals, chosen all
// over the country, and are uploaded like the bot uploads its fixes: one
// changeset per square of the grid (see ChangesetBatcher). A run of apply on
// them then meets the same limits as on the real map: squares, number of
// nodes per changeset, number of edits per run.
public sealed class Seeder(OsmApiClient api, ChangesetTagBuilder tagBuilder, ChangesetSettings changeset, LimitSettings limits)
{
    // Test nodes created when no number is given.
    public const int DefaultCount = 50;

    // The nodes to copy: first one node per correction, to cover as many
    // rules as possible, then the other nodes to fix, one square of the grid
    // after the other in turn, so that the copies spread over the country.
    public static IReadOnlyList<OsmNode> Select(IEnumerable<EditDecision> decisions, int count, double squareDegrees)
    {
        var edits = decisions.Where(decision => decision.IsEdit).ToList();
        HashSet<string> covered = new(StringComparer.Ordinal);
        var picked = edits.Where(edit => edit.Corrections.Count(covered.Add) > 0).Take(count).ToList();

        var squares = edits
            .Except(picked)
            .GroupBy(edit => ChangesetBatcher.Square(edit.Node, squareDegrees))
            .OrderBy(square => square.Key)
            .Select(square => new Queue<EditDecision>(square))
            .ToList();
        while (picked.Count < count && squares.Count > 0)
        {
            picked.AddRange(squares.Take(count - picked.Count).Select(square => square.Dequeue()));
            squares.RemoveAll(square => square.Count == 0);
        }

        return [.. picked.Select(edit => edit.Node)];
    }

    // Returns the ids of the created nodes. A stop request ends the work
    // between two changesets, and so does a refusal of the server once some
    // nodes exist (a sandbox account soon meets the edit rate limit): the
    // nodes created so far are returned.
    public async Task<IReadOnlyList<long>> CreateAsync(IReadOnlyList<OsmNode> nodes, CancellationToken stop)
    {
        List<long> ids = [];
        var batch = 0;
        foreach (var square in ChangesetBatcher.Split(nodes, limits))
        {
            if (stop.IsCancellationRequested)
            {
                break;
            }

            batch++;
            var changesetId = await api.CreateChangesetAsync(tagBuilder.Build(changeset.SeedComment, string.Empty, batch), CancellationToken.None);
            try
            {
                // New nodes are given negative placeholder ids.
                var writes = square.Select((node, index) => new NodeWrite(-(index + 1), 0, node.Lat, node.Lon, node.Tags));
                var results = await api.UploadAsync(changesetId, OsmXml.WriteChange(changesetId, writes, create: true, Banner.ProductVersion()), CancellationToken.None);
                ids.AddRange(results.Values.Select(result => result.NewId));
            }
            catch (OsmApiException ex) when (ids.Count > 0)
            {
                Console.WriteLine($"  {ex.Message}");
                Console.WriteLine($"  Stopped: the {ids.Count} test nodes created so far are kept.");
                break;
            }
            finally
            {
                await api.CloseChangesetAsync(changesetId, CancellationToken.None);
            }

            Console.WriteLine($"  Changeset {changesetId}: {square.Length} test nodes.");
        }

        return ids;
    }
}
