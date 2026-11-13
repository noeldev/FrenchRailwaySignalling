// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;
using SignalBot.Osm;

namespace SignalBot.Planning;

// Lists the nodes to check, grouped by area, from one of three sources:
// - an Overpass query for the whole country, or per area of the filter,
//   optionally limited to the nodes last edited by one mapper or by the
//   mappers who opted in (the normal case);
// - an Overpass answer saved before;
// - a file of node ids read from the OSM server.
// The answers of Overpass are saved in the given folder, when there is one.
// Every area must be listed: a query Overpass does not answer ends the
// command (see OverpassClient), it is never taken for an area without signal.
public sealed class CandidateLoader(OverpassClient overpass, OsmApiClient api, BotSettings settings, OptedInMappers optedIn, TimeProvider clock)
{
    public const string AnswerFilePrefix = "overpass-";

    // Overpass follows the OSM database with a delay of a minute or two. Far
    // behind, its list misses the latest edits: the nodes are still read again
    // before any edit, but signals to fix may be missing from the run.
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

    public async Task<IReadOnlyList<KeyValuePair<string, IReadOnlyList<OsmNode>>>> LoadAsync(CommandLineOptions options, string? saveFolder, CancellationToken cancellationToken)
    {
        if (options.InputPath is { } inputPath)
        {
            Console.WriteLine($"Reading nodes from '{Path.GetFullPath(inputPath)}'...");
            await using var stream = File.OpenRead(inputPath);
            var answer = OverpassReader.Read(stream);
            return [KeyValuePair.Create(answer.AreaName ?? Path.GetFileName(inputPath), answer.Nodes)];
        }

        if (options.NodesPath is { } nodesPath)
        {
            Console.WriteLine($"Reading the nodes listed in '{Path.GetFullPath(nodesPath)}' from the server...");
            var ids = File.ReadLines(nodesPath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => long.Parse(line.Trim()));
            return [KeyValuePair.Create(Path.GetFileName(nodesPath), await api.GetNodesAsync(ids, cancellationToken))];
        }

        var (filter, mapper) = options.Mapper is { } name ? (OverpassClient.UserFilter(name), $", last edited by {name}")
            : options.OptedIn ? (OverpassClient.UidFilter(optedIn.Uids), $", last edited by the {optedIn.Users.Count} opted-in mappers")
            : (string.Empty, string.Empty);

        List<KeyValuePair<string, IReadOnlyList<OsmNode>>> groups = [];
        foreach (var area in new AreaResolver(settings).Resolve(options.Areas))
        {
            if (groups.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.Overpass.DelaySeconds), cancellationToken);
            }

            Console.WriteLine($"Listing the signals of {area.Name ?? area.Code}{mapper} with Overpass...");
            var savePath = saveFolder is null ? null : Path.Combine(saveFolder, $"{AnswerFilePrefix}{area.Code}.json");
            var answer = await overpass.QueryAsync(area.Selector, filter, savePath, cancellationToken);
            Report(answer, area, mapper);

            // The changesets name the area: the country, else the name in
            // OpenStreetMap, else the code.
            groups.Add(KeyValuePair.Create(area.Name ?? answer.AreaName ?? area.Code, answer.Nodes));
        }

        return groups;
    }

    private void Report(OverpassAnswer answer, ResolvedArea area, string mapper)
    {
        Console.WriteLine($"  {answer.Nodes.Count} signals"
            + (answer.DataTime is { } time ? $", OSM data of {time.UtcDateTime:yyyy-MM-dd HH:mm} UTC." : "."));

        if (answer.DataTime is { } dataTime && clock.GetUtcNow() - dataTime > StaleAfter)
        {
            Console.WriteLine($"  Warning: the data of Overpass is {(clock.GetUtcNow() - dataTime).TotalHours:0} hours old; what was mapped or fixed since is not listed.");
        }

        if (answer.Nodes.Count == 0)
        {
            Console.WriteLine(area.Name is null && answer.AreaName is null
                ? $"  Warning: no signal found in {area.Code}{mapper}; Overpass may not know this area."
                : $"  Warning: no signal found in {area.Code}{mapper}.");
        }
    }
}
