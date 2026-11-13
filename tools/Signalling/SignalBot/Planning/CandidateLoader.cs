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
public sealed class CandidateLoader(OverpassClient overpass, OsmApiClient api, BotSettings settings, OptedInMappers optedIn)
{
    public const string AnswerFilePrefix = "overpass-";

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
            if (answer.Nodes.Count == 0)
            {
                Console.WriteLine($"  Warning: no signal found in {area.Code}{mapper}.");
            }

            // The changesets name the area: the country, else the name in
            // OpenStreetMap, else the code.
            groups.Add(KeyValuePair.Create(area.Name ?? answer.AreaName ?? area.Code, answer.Nodes));
        }

        return groups;
    }
}
