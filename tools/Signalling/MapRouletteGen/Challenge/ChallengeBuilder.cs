// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using MapRouletteGen.Osm;
using Signalling.Rules;
using Signalling.Schema;

namespace MapRouletteGen.Challenge;

// Applies the rule table then the corrections of mistyped values to every
// node: a node becomes a task when its tags change. What cannot be fixed
// automatically is only counted, for the JOSM validator and the mappers:
// legacy tagging whose replacement would overwrite a tag, conflicts between
// documented alternatives, and the tags the wiki does not document. The first
// node of each correction is kept as a test sample.
public sealed class ChallengeBuilder(RuleTable table, TagSchema schema, string keyPrefix, string countryPrefix)
{
    private readonly TagRuleApplier _applier = new(table);
    private readonly TokenNormalizer _normalizer = new(schema, keyPrefix, countryPrefix);
    private readonly string _domesticPrefix = countryPrefix + ":";

    public ChallengeContent Build(IReadOnlyList<OsmNode> nodes)
    {
        List<LegacyTask> tasks = [];
        List<string> blocked = [];
        List<string> conflicts = [];
        List<string> undocumented = [];
        Dictionary<string, OsmNode> samples = new(StringComparer.Ordinal);

        foreach (var node in nodes.OrderBy(node => node.Id))
        {
            var ruleFix = _applier.Apply(node.Tags);
            var normalized = _normalizer.Normalize(ruleFix.Fixed);
            var task = new LegacyTask(node, ruleFix with { Fixed = normalized.Tags }, normalized.Fixes);

            if (task.Fix.HasChanges)
            {
                tasks.Add(task);
            }

            foreach (var label in task.Fix.AppliedRules.Concat(task.Fix.BlockedRules.Select(rule => $"{rule} (blocked)"))
                .Concat(task.TokenFixes.Select(fix => $"mistyped value ({fix.Kind})")))
            {
                samples.TryAdd(label, node);
            }

            blocked.AddRange(task.Fix.BlockedRules);
            conflicts.AddRange(_applier.FindConflicts(task.Fix.Fixed));
            if (IsInScope(task.Fix.Fixed))
            {
                undocumented.AddRange(schema.FindUndocumented(task.Fix.Fixed, keyPrefix).Distinct(StringComparer.Ordinal));
            }
        }

        return new ChallengeContent(
            nodes.Count,
            tasks,
            CountByNode(tasks.SelectMany(task => task.Fix.AppliedRules)),
            CountByNode(tasks.SelectMany(task => task.TokenFixes.Select(fix => fix.Kind).Distinct(StringComparer.Ordinal))),
            CountByNode(blocked),
            CountByNode(conflicts),
            CountByNode(undocumented),
            [.. samples.OrderBy(sample => sample.Key, StringComparer.Ordinal)]);
    }

    // The scope of the validator: a value with the country prefix in one of
    // the checked keys.
    private bool IsInScope(IReadOnlyDictionary<string, string> tags) =>
        tags.Any(tag => tag.Key.StartsWith(keyPrefix, StringComparison.Ordinal)
            && tag.Value.StartsWith(_domesticPrefix, StringComparison.OrdinalIgnoreCase));

    // Occurrences of each label (one per node), most frequent first.
    private static IReadOnlyList<KeyValuePair<string, int>> CountByNode(IEnumerable<string> labels) =>
    [
        .. labels
            .GroupBy(label => label, StringComparer.Ordinal)
            .Select(group => KeyValuePair.Create(group.Key, group.Count()))
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal),
    ];
}

// Result of a build: the tasks, the statistics of the challenge texts and one
// sample node per correction (label, node).
public sealed record ChallengeContent(
    int NodeCount,
    IReadOnlyList<LegacyTask> Tasks,
    IReadOnlyList<KeyValuePair<string, int>> RuleCounts,
    IReadOnlyList<KeyValuePair<string, int>> TokenFixCounts,
    IReadOnlyList<KeyValuePair<string, int>> BlockedCounts,
    IReadOnlyList<KeyValuePair<string, int>> ConflictCounts,
    IReadOnlyList<KeyValuePair<string, int>> UndocumentedCounts,
    IReadOnlyList<KeyValuePair<string, OsmNode>> Samples);
