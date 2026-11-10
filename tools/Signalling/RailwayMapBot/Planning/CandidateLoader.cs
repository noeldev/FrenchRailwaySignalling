// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;
using RailwayMapBot.Osm;

namespace RailwayMapBot.Planning;

// Lists the nodes to check, grouped by area, from one of three sources: an
// Overpass query per area (the normal case), an Overpass export, or a file of
// node ids read from the OSM server.
public sealed class CandidateLoader(OverpassClient overpass, OsmApiClient api, BotSettings settings)
{
    public async Task<IReadOnlyList<KeyValuePair<string, IReadOnlyList<OsmNode>>>> LoadAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        if (options.InputPath is { } inputPath)
        {
            Console.WriteLine($"Reading nodes from '{inputPath}'...");
            await using var stream = File.OpenRead(inputPath);
            return [KeyValuePair.Create(Path.GetFileName(inputPath), OverpassReader.Read(stream))];
        }

        if (options.NodesPath is { } nodesPath)
        {
            Console.WriteLine($"Reading the nodes listed in '{nodesPath}' from the server...");
            var ids = File.ReadLines(nodesPath).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => long.Parse(line.Trim()));
            return [KeyValuePair.Create(Path.GetFileName(nodesPath), await api.GetNodesAsync(ids, cancellationToken))];
        }

        List<KeyValuePair<string, IReadOnlyList<OsmNode>>> groups = [];
        foreach (var area in SelectAreas(options.Areas))
        {
            if (groups.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.Overpass.DelaySeconds), cancellationToken);
            }

            Console.WriteLine($"Listing the nodes of {area.Name} ({area.Code}) with Overpass...");
            var nodes = await overpass.QueryAsync(area.Code, cancellationToken);
            if (nodes.Count == 0)
            {
                Console.WriteLine($"  Warning: no node found in {area.Code}; check the area code.");
            }

            groups.Add(KeyValuePair.Create(area.Name, nodes));
        }

        return groups;
    }

    private IEnumerable<AreaSettings> SelectAreas(IReadOnlyList<string> codes)
    {
        if (codes.Count == 0)
        {
            return settings.Areas;
        }

        var unknown = codes.Where(code => settings.Areas.All(area => !area.Code.Equals(code, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"Unknown area: {string.Join(", ", unknown)}. Configured: {string.Join(", ", settings.Areas.Select(area => area.Code))}");
        }

        return settings.Areas.Where(area => codes.Contains(area.Code, StringComparer.OrdinalIgnoreCase));
    }
}
