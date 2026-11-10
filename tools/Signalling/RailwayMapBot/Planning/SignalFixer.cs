// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Rules;
using Signalling.Schema;

namespace RailwayMapBot.Planning;

// Computes the fix of one signal exactly as the MapRoulette challenge does:
// the rule table first, then the corrections of mistyped values against the
// documented scheme. All the fixes of a node are applied together, so a node
// is edited once.
public sealed class SignalFixer(RuleTable table, TagSchema schema, string keyPrefix, string countryPrefix, bool fixMistypedValues)
{
    private readonly TagRuleApplier _applier = new(table);
    private readonly TokenNormalizer _normalizer = new(schema, keyPrefix, countryPrefix);

    // Labels of the replacements that guess the tagging from an incomplete one.
    private readonly HashSet<string> _suggestions = Labels(table, entry => entry.Suggest);

    // Labels of the replacements whose note asks the mapper for a complement
    // (a caption to complete, a board to map on its own node...). The notes of
    // the redundant tags only explain why they are removed.
    private readonly HashSet<string> _incomplete = Labels(table, entry => !entry.Suggest && !string.IsNullOrWhiteSpace(entry.Note));

    public SignalFix Compute(IReadOnlyDictionary<string, string> tags)
    {
        var fix = _applier.Apply(tags);
        var result = fix.Fixed;
        List<string> corrections = [.. fix.AppliedRules];

        if (fixMistypedValues)
        {
            var normalized = _normalizer.Normalize(fix.Fixed);
            result = normalized.Tags;
            corrections.AddRange(normalized.Fixes.Select(entry => $"mistyped value ({entry.Kind})").Distinct(StringComparer.Ordinal));
        }

        return new SignalFix(result, corrections, fix.AppliedRules.Any(_suggestions.Contains), fix.AppliedRules.Any(_incomplete.Contains));
    }

    private static HashSet<string> Labels(RuleTable table, Func<TagReplacement, bool> filter) =>
        [.. table.TagReplacements.Where(filter).Select(entry => RuleTable.Describe(entry.LegacyTags))];
}

// The tags of a signal once fixed, the corrections made (the legacy tagging
// replaced, as the challenge and the validator name it), whether one of them
// is a guess and whether one of them needs a manual complement.
public sealed record SignalFix(
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<string> Corrections,
    bool IsSuggestion,
    bool NeedsComplement);
