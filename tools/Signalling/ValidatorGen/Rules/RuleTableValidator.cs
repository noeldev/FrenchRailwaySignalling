// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Rules;
using Signalling.Schema;

namespace ValidatorGen.Rules;

// Checks the rule table against the documented scheme, so a stale entry fails
// the generation instead of producing a rule that flags valid tagging or
// suggests undocumented tagging:
// - the legacy side of a replacement must be undocumented;
// - the replacement side (set, rename, default) and the tags of a conflict
//   must be documented, for the keys of the scheme (under the key prefix):
//   another key (ref...) is out of the wiki tables, and an accepted key takes
//   any value;
// - a suggestion has a fix;
// - an accepted key is not documented (else it is stale);
// - a replacement that extends the match of another one comes before it, so
//   the validator can skip the general one where the specific one matched;
// - a replacement fixes something, or reports with a note;
// - a redundancy only removes tags it matches, and keeps the others.
public static class RuleTableValidator
{
    public static IReadOnlyList<string> Validate(RuleTable table, TagSchema schema, string keyPrefix)
    {
        var accepted = table.AcceptedKeys.ToHashSet(StringComparer.Ordinal);
        var replacements = table.TagReplacements;
        List<string> errors =
        [
            .. table.ValueReplacements.SelectMany(entry => ValidateValueReplacement(entry, schema)),
            .. table.TagReplacements.SelectMany(entry => ValidateTagReplacement(entry, schema, keyPrefix, accepted)),
            .. table.AcceptedKeys.Where(schema.IsDocumentedKey)
                .Select(key => $"accepted key {key}: it is documented, so it must be removed from acceptedKeys."),
            .. replacements.SelectMany((entry, index) => replacements.Skip(index + 1)
                .Where(other => other.Match.Count > entry.Match.Count
                    && entry.Match.All(tag => other.Match.TryGetValue(tag.Key, out var value) && value == tag.Value))
                .Select(other => $"tag replacement {RuleTable.Describe(other.PresentTags)}: it extends {RuleTable.Describe(entry.PresentTags)}, so it must come before it.")),
            .. table.Conflicts.SelectMany(entry => ValidateConflict(entry, schema)),
            .. table.Redundancies.SelectMany(ValidateRedundancy),
        ];

        return errors;
    }

    private static IEnumerable<string> ValidateValueReplacement(ValueReplacement entry, TagSchema schema)
    {
        var label = $"value replacement {entry.Key}={entry.From}";
        if (!schema.IsDocumentedKey(entry.Key))
        {
            yield return $"{label}: the key is not documented.";
        }
        else if (schema.IsDocumentedValue(entry.Key, entry.From))
        {
            yield return $"{label}: {entry.From} is documented, so it is not legacy.";
        }

        if (!schema.IsDocumentedValue(entry.Key, entry.To))
        {
            yield return $"{label}: the replacement {entry.To} is not documented.";
        }
    }

    private static IEnumerable<string> ValidateTagReplacement(TagReplacement entry, TagSchema schema, string keyPrefix, IReadOnlySet<string> accepted)
    {
        var label = $"tag replacement {RuleTable.Describe(entry.PresentTags)}";
        if (!entry.PresentTags.Any())
        {
            yield return "tag replacement: \"match\" needs at least one present tag.";
            yield break;
        }

        if (entry.PresentTags.All(tag => schema.IsDocumentedValue(tag.Key, tag.Value)))
        {
            yield return $"{label}: every matched tag is documented, so it is not legacy.";
        }

        bool InScheme(string key) => key.StartsWith(keyPrefix, StringComparison.Ordinal) && !accepted.Contains(key);

        foreach (var tag in entry.Set.Where(tag => InScheme(tag.Key) && !schema.IsDocumentedValue(tag.Key, tag.Value)))
        {
            yield return $"{label}: the replacement {tag.Key}={tag.Value} is not documented.";
        }

        foreach (var rename in entry.Rename.Where(rename => InScheme(rename.Value) && !schema.IsDocumentedKey(rename.Value)))
        {
            yield return $"{label}: the renamed key {rename.Value} is not documented.";
        }

        foreach (var tag in entry.Default.Where(tag => InScheme(tag.Key) && !schema.IsDocumentedValue(tag.Key, tag.Value)))
        {
            yield return $"{label}: the default {tag.Key}={tag.Value} is not documented.";
        }

        foreach (var key in entry.Default.Keys.Where(entry.Set.ContainsKey))
        {
            yield return $"{label}: {key} is both set and defaulted.";
        }


        if (entry.IsReportOnly && string.IsNullOrWhiteSpace(entry.Note))
        {
            yield return $"{label}: nothing to fix (set, rename, remove and default are empty) and no note to report.";
        }

        if (entry.Suggest && entry.IsReportOnly)
        {
            yield return $"{label}: a suggestion needs a fix that applies it.";
        }
    }

    private static IEnumerable<string> ValidateRedundancy(TagReplacement entry)
    {
        var label = $"redundancy {RuleTable.Describe(entry.PresentTags)}";
        if (entry.Set.Count > 0 || entry.Rename.Count > 0 || entry.Default.Count > 0 || entry.Suggest)
        {
            yield return $"{label}: only \"match\", \"remove\" and \"note\" are allowed.";
        }

        if (entry.Remove.Count == 0)
        {
            yield return $"{label}: nothing to remove.";
        }

        var present = entry.PresentTags.Select(tag => tag.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in entry.Remove.Where(key => !present.Contains(key)))
        {
            yield return $"{label}: the removed key {key} is not a matched tag.";
        }

        if (entry.PresentTags.All(tag => entry.Remove.Contains(tag.Key)))
        {
            yield return $"{label}: no matched tag is kept to imply the removed ones.";
        }
    }

    private static IEnumerable<string> ValidateConflict(TagConflict entry, TagSchema schema)
    {
        var label = $"conflict {RuleTable.Describe(entry.Tags)}";
        if (entry.Tags.Count < 2)
        {
            yield return $"{label}: a conflict needs at least two tags.";
        }

        foreach (var tag in entry.Tags.Where(tag => !schema.IsDocumentedValue(tag.Key, tag.Value)))
        {
            yield return $"{label}: {tag.Key}={tag.Value} is not documented.";
        }
    }
}
