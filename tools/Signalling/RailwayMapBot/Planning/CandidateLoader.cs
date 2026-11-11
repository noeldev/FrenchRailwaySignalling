// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;
using RailwayMapBot.Osm;

namespace RailwayMapBot.Planning;

// Lists the nodes to check, grouped by area, from one of three sources: an
// Overpass query per area (the normal case), an Overpass answer saved before,
// or a file of node ids read from the OSM server. The answers of Overpass are
// saved in the given folder, when there is one.
public sealed class CandidateLoader(OverpassClient overpass, OsmApiClient api, BotSettings settings)
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

        List<KeyValuePair<string, IReadOnlyList<OsmNode>>> groups = [];
        foreach (var area in new AreaResolver(settings).Resolve(options.Areas))
        {
            if (groups.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.Overpass.DelaySeconds), cancellationToken);
            }

            Console.WriteLine($"Listing the nodes of {area.Name ?? area.Code} with Overpass...");
            var savePath = saveFolder is null ? null : Path.Combine(saveFolder, $"{AnswerFilePrefix}{area.Code}.json");
            var answer = await overpass.QueryAsync(area.Selector, savePath, cancellationToken);
            if (answer.Nodes.Count == 0)
            {
                Console.WriteLine($"  Warning: no node found in {area.Code}; check the area code.");
            }

            // The changesets name the area: the configured name, else the one
            // of OpenStreetMap, else the code.
            groups.Add(KeyValuePair.Create(area.Name ?? answer.AreaName ?? area.Code, answer.Nodes));
        }

        return groups;
    }
}
