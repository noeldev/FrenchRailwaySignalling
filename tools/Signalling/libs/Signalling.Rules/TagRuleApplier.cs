// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace Signalling.Rules;

// Applies the rule table to the tags of one OSM object, the way the JOSM
// validator fixes would be applied one after the other until none is left:
// - a value replacement renames a token of a ";" separated value (FR:CV;FR:M
//   becomes FR:Cv;FR:M), the edge spaces and empty entries being dropped;
// - a tag replacement first renames keys, then removes keys, then sets tags,
//   in the order of its MapCSS fix declarations.
// A fix can enable another one (a tag replacement producing a legacy value),
// so the rules are applied again until the tags no longer change.
public sealed class TagRuleApplier(RuleTable table)
{
    private const char ValueSeparator = ';';

    // Guard against a rule table whose fixes would undo each other.
    private const int MaxPasses = 10;

    public TagFix Apply(IReadOnlyDictionary<string, string> source)
    {
        var tags = new Dictionary<string, string>(source, StringComparer.Ordinal);
        List<string> applied = [];

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var changed = false;

            foreach (var entry in table.TagReplacements.Where(entry => Matches(entry, tags)))
            {
                ApplyTagReplacement(entry, tags);
                applied.Add(RuleTable.Describe(entry.PresentTags));
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
                return new TagFix(source, tags, [.. applied.Distinct()]);
            }
        }

        throw new InvalidOperationException(
            $"The rule table does not converge on {RuleTable.Describe(source)}: check the replacements for a cycle.");
    }

    // Documented alternatives found together (not fixable automatically).
    public IReadOnlyList<string> FindConflicts(IReadOnlyDictionary<string, string> tags) =>
    [
        .. table.Conflicts
            .Where(entry => entry.Tags.All(tag => tags.TryGetValue(tag.Key, out var value) && value == tag.Value))
            .Select(entry => RuleTable.Describe(entry.Tags)),
    ];

    private static bool Matches(TagReplacement entry, Dictionary<string, string> tags) =>
        entry.PresentTags.Any()
        && entry.Match.All(tag => tag.Value is null
            ? !tags.ContainsKey(tag.Key)
            : tags.TryGetValue(tag.Key, out var value) && value == tag.Value);

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

        tags[entry.Key] = string.Join(ValueSeparator, tokens
            .Select(token => token == entry.From ? entry.To : token.Trim())
            .Where(token => token.Length > 0));
        return true;
    }
}
