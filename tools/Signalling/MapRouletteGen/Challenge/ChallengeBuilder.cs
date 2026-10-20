// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using MapRouletteGen.Osm;
using Signalling.Rules;

namespace MapRouletteGen.Challenge;

// Applies the rule table to every node: a node becomes a task when at least
// one rule changes its tags. Conflicts between documented alternatives cannot
// be fixed automatically; they are only counted, the JOSM validator reports
// them.
public sealed class ChallengeBuilder(RuleTable table)
{
    private readonly TagRuleApplier _applier = new(table);

    public ChallengeContent Build(IReadOnlyList<OsmNode> nodes)
    {
        List<LegacyTask> tasks = [];
        Dictionary<string, int> conflicts = new(StringComparer.Ordinal);

        foreach (var node in nodes.OrderBy(node => node.Id))
        {
            var fix = _applier.Apply(node.Tags);
            if (fix.HasChanges)
            {
                tasks.Add(new LegacyTask(node, fix));
            }

            foreach (var conflict in _applier.FindConflicts(fix.Fixed))
            {
                conflicts[conflict] = conflicts.GetValueOrDefault(conflict) + 1;
            }
        }

        // Number of nodes fixed by each rule, most frequent first.
        var ruleCounts = tasks
            .SelectMany(task => task.Fix.AppliedRules)
            .GroupBy(rule => rule, StringComparer.Ordinal)
            .Select(group => KeyValuePair.Create(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        return new ChallengeContent(nodes.Count, tasks, ruleCounts, conflicts);
    }
}

// Result of a build: the tasks and the statistics of the challenge texts.
public sealed record ChallengeContent(
    int NodeCount,
    IReadOnlyList<LegacyTask> Tasks,
    IReadOnlyList<KeyValuePair<string, int>> RuleCounts,
    IReadOnlyDictionary<string, int> ConflictCounts);
