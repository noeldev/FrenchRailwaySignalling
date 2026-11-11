// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using RailwayMapBot.Core;
using RailwayMapBot.Osm;
using RailwayMapBot.Planning;
using RailwayMapBot.Reporting;

namespace RailwayMapBot.Execution;

// Runs a planner over the listed nodes, area by area and batch by batch (see
// ChangesetBatcher):
// 1. read the nodes of the batch again from the server, just before deciding;
// 2. decide on that fresh data what to edit and what to skip;
// 3. write the edits to the journal (and to an osmChange file), then upload
//    them in one changeset, each node with the version it was read at.
// The server refuses the whole upload when a node changed in between: the
// batch is then read and decided again, never forced. Since every decision is
// taken on fresh data, a run can be stopped and started again at any time:
// what is already fixed is simply no longer a candidate.
public sealed class EditRunner(
    OsmApiClient api,
    IEditPlanner planner,
    ContributorGuard guard,
    RunJournal journal,
    RunSummary summary,
    ChangesetTagBuilder tagBuilder,
    LimitSettings limits,
    RunMode mode,
    string commentTemplate,
    string runFolder)
{
    private const int MaxRateLimitWaits = 3;

    private int _batch;

    public async Task RunAsync(IReadOnlyList<CandidateGroup> groups, int editLimit, CancellationToken stop)
    {
        try
        {
            foreach (var group in groups)
            {
                summary.Areas.Add(new AreaSummary(group.Name, group.Listed, group.Nodes.Count));
                foreach (var chunk in ChangesetBatcher.Split(group.Nodes, limits))
                {
                    if (stop.IsCancellationRequested)
                    {
                        summary.Outcome = "stopped by the operator";
                        return;
                    }

                    var remaining = editLimit - summary.EditCount;
                    if (remaining <= 0)
                    {
                        summary.Outcome = "stopped: edit limit reached";
                        return;
                    }

                    if (await ProcessBatchAsync(group.Name, chunk, remaining, stop) && mode == RunMode.Write)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(limits.DelayBetweenChangesetsSeconds), stop);
                    }
                }
            }

            summary.Outcome = "completed";
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            summary.Outcome = "stopped by the operator";
        }
        catch (OsmApiException ex) when (ex.IsRateLimited)
        {
            summary.Outcome = "stopped: the server limits the edit rate of the account, run again later";
            summary.Failed = true;
        }
        catch (Exception ex) when (ex is OsmApiException or HttpRequestException or TaskCanceledException)
        {
            summary.Outcome = $"failed: {ex.Message}";
            summary.Failed = true;
        }
    }

    // Returns true when a changeset was uploaded.
    private async Task<bool> ProcessBatchAsync(string area, OsmNode[] listed, int remaining, CancellationToken stop)
    {
        var batch = ++_batch;
        var decisions = await DecideAsync(listed, stop);
        var edits = decisions.Where(decision => decision.IsEdit).Take(remaining).ToList();
        long? changesetId = null;
        var uploaded = false;

        if (mode == RunMode.Write && edits.Count > 0)
        {
            // From here on the batch is completed whatever happens: an open
            // changeset is never left behind.
            changesetId = await api.CreateChangesetAsync(tagBuilder.Build(commentTemplate, area, batch), CancellationToken.None);
            try
            {
                for (var attempt = 0; edits.Count > 0; attempt++)
                {
                    WriteBatch(batch, area, changesetId, edits);
                    try
                    {
                        var results = await UploadAsync(changesetId.Value, edits);
                        journal.Write(new JournalRecord("uploaded")
                        {
                            Batch = batch,
                            Changeset = changesetId,
                            Versions = results.ToDictionary(result => result.Key, result => result.Value.NewVersion),
                        });
                        uploaded = true;
                        break;
                    }
                    catch (OsmApiException ex) when (ex.IsConflict && attempt < limits.ConflictRetries)
                    {
                        Console.WriteLine($"  Batch {batch}: a node changed meanwhile ({ex.Message}). Reading the batch again...");
                        decisions = await DecideAsync(listed, CancellationToken.None);
                        edits = decisions.Where(decision => decision.IsEdit).Take(remaining).ToList();
                    }
                }
            }
            finally
            {
                summary.Changesets.Add(new ChangesetSummary(batch, area, changesetId, edits.Count, uploaded));
                await CloseAsync(changesetId.Value);
            }
        }
        else if (edits.Count > 0)
        {
            WriteBatch(batch, area, null, edits);
            summary.Changesets.Add(new ChangesetSummary(batch, area, null, edits.Count, Done: true));
        }

        foreach (var edit in edits)
        {
            summary.AddEdit(edit);
        }

        foreach (var skipped in decisions.Where(decision => !decision.IsEdit))
        {
            summary.AddSkip(skipped);
            journal.Write(new JournalRecord("skip")
            {
                Node = skipped.Node.Id,
                User = skipped.Node.User,
                Reason = skipped.Skip.ToString(),
                Corrections = skipped.Corrections.Count > 0 ? skipped.Corrections : null,
            });
        }

        Console.WriteLine($"  Batch {batch} ({area}): {edits.Count} edited, {decisions.Count(decision => !decision.IsEdit)} left untouched"
            + (changesetId is null ? string.Empty : $", changeset {changesetId}"));
        return uploaded;
    }

    private async Task<IReadOnlyList<EditDecision>> DecideAsync(OsmNode[] listed, CancellationToken cancellationToken)
    {
        if (mode == RunMode.Offline)
        {
            return [.. listed.Select(planner.Decide)];
        }

        var fresh = await api.GetNodesAsync(listed.Select(node => node.Id), cancellationToken);
        return await guard.ApplyAsync([.. fresh.Select(planner.Decide)], cancellationToken);
    }

    // Journal first, then the osmChange file: both exist before the upload.
    private void WriteBatch(int batch, string area, long? changesetId, List<EditDecision> edits)
    {
        journal.Write(new JournalRecord("batch")
        {
            Batch = batch,
            Area = area,
            Changeset = changesetId,
            Edits =
            [
                .. edits.Select(edit => new JournalEdit(
                    edit.Node.Id, edit.Node.Version, edit.Node.Lat, edit.Node.Lon, edit.Node.User, edit.Node.Timestamp,
                    edit.Node.Tags, edit.NewTags!, edit.Corrections)),
            ],
        });

        OsmXml.Save(BuildChange(changesetId ?? 0, edits), Path.Combine(runFolder, $"changes-{batch:000}.osc"));
    }

    private async Task<IReadOnlyDictionary<long, NodeResult>> UploadAsync(long changesetId, List<EditDecision> edits)
    {
        var change = BuildChange(changesetId, edits);
        for (var waits = 0; ; waits++)
        {
            try
            {
                return await api.UploadAsync(changesetId, change, CancellationToken.None);
            }
            catch (OsmApiException ex) when (ex.IsRateLimited
                && waits < MaxRateLimitWaits
                && ex.RetryAfter is { } delay
                && delay.TotalSeconds <= limits.MaxRateLimitWaitSeconds)
            {
                // Refused before anything was written: sending it again is safe.
                Console.WriteLine($"  The server limits the edit rate: waiting {Math.Ceiling(delay.TotalSeconds)} s...");
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, CancellationToken.None);
            }
        }
    }

    private static XDocument BuildChange(long changesetId, List<EditDecision> edits) =>
        OsmXml.WriteChange(
            changesetId,
            edits.Select(edit => new NodeWrite(edit.Node.Id, edit.Node.Version, edit.Node.Lat, edit.Node.Lon, edit.NewTags!)),
            create: false,
            Banner.ProductVersion());

    private async Task CloseAsync(long changesetId)
    {
        try
        {
            await api.CloseChangesetAsync(changesetId, CancellationToken.None);
        }
        catch (Exception ex) when (ex is OsmApiException or HttpRequestException or TaskCanceledException)
        {
            // The server closes an idle changeset by itself after an hour.
            Console.Error.WriteLine($"  Warning: changeset {changesetId} could not be closed ({ex.Message}).");
        }
    }
}
