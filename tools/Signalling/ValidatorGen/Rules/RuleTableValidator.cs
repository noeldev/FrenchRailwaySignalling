// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using ValidatorGen.Schema;

namespace ValidatorGen.Rules;

// Checks the rule table against the documented scheme, so a stale entry fails
// the generation instead of producing a rule that flags valid tagging or
// suggests undocumented tagging:
// - the legacy side of a replacement must be undocumented;
// - the replacement side and the tags of a conflict must be documented.
public static class RuleTableValidator
{
    public static IReadOnlyList<string> Validate(RuleTable table, TagSchema schema)
    {
        List<string> errors =
        [
            .. table.ValueReplacements.SelectMany(entry => ValidateValueReplacement(entry, schema)),
            .. table.TagReplacements.SelectMany(entry => ValidateTagReplacement(entry, schema)),
            .. table.Conflicts.SelectMany(entry => ValidateConflict(entry, schema)),
        ];

        return errors;
    }

    public static string Describe(IEnumerable<KeyValuePair<string, string>> tags) =>
        string.Join(" + ", tags.Select(tag => $"{tag.Key}={tag.Value}"));

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

    private static IEnumerable<string> ValidateTagReplacement(TagReplacement entry, TagSchema schema)
    {
        var label = $"tag replacement {Describe(entry.PresentTags)}";
        if (!entry.PresentTags.Any())
        {
            yield return "tag replacement: \"match\" needs at least one present tag.";
            yield break;
        }

        if (entry.PresentTags.All(tag => schema.IsDocumentedValue(tag.Key, tag.Value)))
        {
            yield return $"{label}: every matched tag is documented, so it is not legacy.";
        }

        foreach (var tag in entry.Set.Where(tag => !schema.IsDocumentedValue(tag.Key, tag.Value)))
        {
            yield return $"{label}: the replacement {tag.Key}={tag.Value} is not documented.";
        }

        foreach (var rename in entry.Rename.Where(rename => !schema.IsDocumentedKey(rename.Value)))
        {
            yield return $"{label}: the renamed key {rename.Value} is not documented.";
        }

        if (entry.Set.Count == 0 && entry.Rename.Count == 0 && entry.Remove.Count == 0)
        {
            yield return $"{label}: nothing to fix (set, rename and remove are empty).";
        }
    }

    private static IEnumerable<string> ValidateConflict(TagConflict entry, TagSchema schema)
    {
        var label = $"conflict {Describe(entry.Tags)}";
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
