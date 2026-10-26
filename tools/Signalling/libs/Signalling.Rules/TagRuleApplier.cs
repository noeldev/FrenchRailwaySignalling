// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Rules;

// Applies the rule table to the tags of one OSM object, the way the JOSM
// validator fixes would be applied one after the other until none is left:
// - a value replacement renames a token of a ";" separated value (FR:CV;FR:M
//   becomes FR:Cv;FR:M), or drops it when the replacement is empty, the edge
//   spaces and empty entries being dropped;
// - a tag replacement first renames keys, then removes keys, then sets tags
//   and finally gives their default value to the keys still absent, in the
//   order of its MapCSS fix declarations. It is skipped (blocked) when it would
//   overwrite a tag it does not match, like its MapCSS fix, and when it only
//   reports the legacy tagging (left to fix by hand, like a blocked one).
// The redundant tags are removed like legacy ones.
// A fix can enable another one (a tag replacement producing a legacy value, or
// a value replacement unblocking a tag replacement), so the rules are applied
// again until the tags no longer change.
public sealed class TagRuleApplier(RuleTable table)
{
    private const char ValueSeparator = ';';

    // Guard against a rule table whose fixes would undo each other.
    private const int MaxPasses = 10;

    public TagFix Apply(IReadOnlyDictionary<string, string> source)
    {
        var tags = new Dictionary<string, string>(source, StringComparer.Ordinal);
        List<string> applied = [];
        List<string> notes = [];

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var changed = false;

            foreach (var entry in Replacements.Where(entry => !entry.IsReportOnly && Matches(entry, tags) && !IsBlocked(entry, tags)))
            {
                ApplyTagReplacement(entry, tags);
                applied.Add(RuleTable.Describe(entry.LegacyTags));
                if (!string.IsNullOrWhiteSpace(entry.Note))
                {
                    notes.Add(entry.Note);
                }

                changed = true;
            }

            foreach (var entry in table.ValueReplacements)
            {
                if (TryReplaceValue(entry, tags))
                {
                    applied.Add($"{entry.Key}={entry.From}");
                    changed = true;
                }
            }

            if (!changed)
            {
                List<string> blocked =
                [
                    .. table.TagReplacements
                        .Where(entry => Matches(entry, tags) && (entry.IsReportOnly || IsBlocked(entry, tags)))
                        .Select(entry => RuleTable.Describe(entry.LegacyTags)),
                ];

                return new TagFix(source, tags, [.. applied.Distinct()], [.. notes.Distinct()], blocked);
            }
        }

        throw new InvalidOperationException(
            $"The rule table does not converge on {RuleTable.Describe(source)}: check the replacements for a cycle.");
    }

    // The tag replacements, then the redundant tags to remove.
    private IEnumerable<TagReplacement> Replacements => table.TagReplacements.Concat(table.Redundancies);

    // Documented alternatives found together (not fixable automatically).
    public IReadOnlyList<string> FindConflicts(IReadOnlyDictionary<string, string> tags) =>
    [
        .. table.Conflicts
            .Where(entry => entry.Tags.All(tag => tags.TryGetValue(tag.Key, out var value) && value == tag.Value))
            .Select(entry => RuleTable.Describe(entry.Tags)),
    ];

    private static bool Matches(TagReplacement entry, Dictionary<string, string> tags) =>
        entry.PresentTags.Any()
        && entry.Match.All(tag => tag.Value switch
        {
            null => !tags.ContainsKey(tag.Key),
            RuleTable.AnyValue => tags.ContainsKey(tag.Key),
            _ => tags.TryGetValue(tag.Key, out var value) && value == tag.Value,
        });

    private bool IsBlocked(TagReplacement entry, Dictionary<string, string> tags) =>
        entry.GuardedSets.Any(tag => tags.TryGetValue(tag.Key, out var value)
            && !table.AcceptedValues(tag.Key, tag.Value).Contains(value))
        || entry.GuardedTargets.Any(tags.ContainsKey);

    private static void ApplyTagReplacement(TagReplacement entry, Dictionary<string, string> tags)
    {
        foreach (var (from, to) in entry.Rename)
        {
            if (tags.Remove(from, out var value))
            {
                tags[to] = value;
            }
        }

        foreach (var key in entry.Remove)
        {
            tags.Remove(key);
        }

        foreach (var (key, value) in entry.Set)
        {
            tags[key] = value;
        }

        foreach (var (key, value) in entry.Default)
        {
            tags.TryAdd(key, value);
        }
    }

    private static bool TryReplaceValue(ValueReplacement entry, Dictionary<string, string> tags)
    {
        if (!tags.TryGetValue(entry.Key, out var value))
        {
            return false;
        }

        var tokens = value.Split(ValueSeparator);
        if (!tokens.Contains(entry.From, StringComparer.Ordinal))
        {
            return false;
        }

        var replaced = string.Join(ValueSeparator, tokens
            .Select(token => token == entry.From ? entry.To : token.Trim())
            .Where(token => token.Length > 0));

        // An empty replacement drops the token, and the key with its last one.
        if (replaced.Length == 0)
        {
            tags.Remove(entry.Key);
        }
        else
        {
            tags[entry.Key] = replaced;
        }

        return true;
    }
}
