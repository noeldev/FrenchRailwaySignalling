// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Rules;
using Signalling.Schema;
using ValidatorGen.Core;
using ValidatorGen.MapCss;
using static ValidatorGen.MapCss.MapCssSyntax;

namespace ValidatorGen.Generation;

// Turns the documented scheme and the rule table into MapCSS rules:
// 1. a scope rule that marks the nodes to check (a value with the country
//    prefix in one of the checked keys);
// 2. one rule per legacy value and per legacy tagging, with its fix. The
//    message only names the legacy tagging, to keep the JOSM dialogs narrow;
//    the replacement is carried by the fix. The fix of a legacy tagging is
//    guarded so it never overwrites a tag it does not match; one more rule
//    reports, without a fix, the legacy tagging a guard keeps;
// 3. one rule per conflict between documented alternatives, and one warning
//    per redundant tag, with a fix that removes it;
// 4. one rule for keys the wiki does not document;
// 5. one rule for values the wiki does not list, for every closed key. Values
//    of another scheme (CH-FDV:, ETCS:...) are out of scope and never reported.
// Legacy keys and values reported by 2 are left out of 4 and 5, so a legacy tag
// is reported once, with its fix. Only those of a rule matched by a single tag
// are left out: a rule with more conditions does not report every occurrence.
//
// The assertMatch/assertNoMatch declarations are self-tests run by JOSM and
// Osmose-QA against the rule itself; they never inspect map data.
public sealed class ValidatorRuleBuilder(TagSchema schema, RuleTable table, ValidatorSettings settings)
{
    private const int ValueSampleCount = 2;
    private const string UndocumentedSuffix = "_undocumented";

    // A value that no replacement sets, for the self-tests of the guards.
    private const string ConflictingValue = "XX:conflicting";

    // A token of another scheme: a namespace prefix (same shape as
    // TagSchema.HasNamespacePrefix) that is not the country prefix.
    private const string ForeignPrefixPattern = "[A-Z][A-Za-z]*(?:-[A-Za-z]+)*:[^;]*";

    private static readonly IReadOnlySet<string> NoValues = new HashSet<string>(StringComparer.Ordinal);

    private readonly string _scopeClass = settings.CountryPrefix.ToLowerInvariant() + "_signal";

    public IReadOnlyList<MapCssRule> Build()
    {
        var scope = FindScopeSample();
        return
        [
            BuildScopeRule(),
            .. table.ValueReplacements.Select(BuildValueReplacementRule),
            .. table.TagReplacements.Select(BuildTagReplacementRule),
            .. table.TagReplacements.Where(entry => entry.GuardedSets.Any() || entry.GuardedTargets.Any()).Select(BuildGuardRule),
            .. table.Conflicts.Select(BuildConflictRule),
            .. table.Redundancies.Select(BuildRedundancyRule),
            BuildUndocumentedKeyRule(scope),
            BuildUndocumentedValueRule(scope),
        ];
    }

    private MapCssRule BuildScopeRule()
    {
        var selector = $"node[/^{EscapeRegex(settings.KeyPrefix)}/=~/^{CaseInsensitive(settings.CountryPrefix)}:/]";
        return new MapCssRule(
            $"Scope: nodes with a {settings.CountryPrefix}: value in a {settings.KeyPrefix}* key",
            [selector],
            [$"set .{_scopeClass}"]);
    }

    private MapCssRule BuildValueReplacementRule(ValueReplacement entry, int index)
    {
        var selector = $"node[{Quote(entry.Key)}=~/{TokenPattern(EscapeRegex(entry.From))}/]";
        var separator = TagSchema.ValueSeparator.ToString();

        // Replace the token inside ";list;" then drop the empty entries, which
        // works for a single value and for a list alike, and drops the token
        // when the replacement is empty (JOSM removes a tag set to "").
        var listed = $"concat({Quote(separator)}, tag({Quote(entry.Key)}), {Quote(separator)})";
        var replaced = $"replace({listed}, {Quote(separator + entry.From + separator)}, {Quote(separator + entry.To + separator)})";
        var fix = $"concat({Quote(entry.Key + "=")}, join_list({Quote(separator)}, trim_list(split({Quote(separator)}, {replaced}))))";

        List<string> declarations =
        [
            $"throwError: tr(\"{{0}}={{1}} is deprecated\", {Quote(entry.Key)}, {Quote(entry.From)})",
            $"fixAdd: {fix}",
            $"assertMatch: {Quote(AssertNode([KeyValuePair.Create(entry.Key, entry.From)]))}",
        ];

        // An empty replacement drops the token: no replacement value to test.
        if (entry.To.Length > 0)
        {
            declarations.Add($"assertNoMatch: {Quote(AssertNode([KeyValuePair.Create(entry.Key, entry.To)]))}");
        }

        return new MapCssRule(index == 0 ? "Legacy values, renamed in place" : null, [selector], declarations);
    }

    private MapCssRule BuildTagReplacementRule(TagReplacement entry, int index)
    {
        // A set tag must be absent (an empty value for Osmose-QA) or hold an
        // accepted value; a rename target must be absent.
        var guards = entry.GuardedSets
            .Select(tag => $"[{Quote(tag.Key)}!~/^(?!(?:{Alternation(table.AcceptedValues(tag.Key, tag.Value).Order(StringComparer.Ordinal))}|)$)/]")
            .Concat(entry.GuardedTargets.Select(key => $"[!{Quote(key)}]"));

        List<string> declarations =
        [
            DeprecatedMessage(entry),
            .. entry.Rename.Select(rename => $"fixChangeKey: {Quote($"{rename.Key}=>{rename.Value}")}"),
            .. entry.Remove.Select(key => $"fixRemove: {Quote(key)}"),
            .. entry.Set.Select(tag => $"fixAdd: {Quote($"{tag.Key}={tag.Value}")}"),
            $"assertMatch: {Quote(AssertNode(SampleTags(entry)))}",
        ];

        if (entry.Set.Count > 0)
        {
            declarations.Add($"assertNoMatch: {Quote(AssertNode(entry.Set))}");
        }

        declarations.AddRange(GuardSamples(entry).Select(sample => $"assertNoMatch: {Quote(AssertNode([.. SampleTags(entry), sample]))}"));

        return new MapCssRule(
            index == 0 ? "Legacy tagging, restructured" : null,
            [MatchSelector(entry) + string.Concat(guards)],
            declarations);
    }

    private static string DeprecatedMessage(TagReplacement entry)
    {
        var legacy = Quote(RuleTable.Describe(entry.PresentTags));
        return string.IsNullOrWhiteSpace(entry.Note)
            ? $"throwError: tr(\"{{0}} is deprecated\", {legacy})"
            : $"throwError: tr(\"{{0}} is deprecated: {{1}}\", {legacy}, {Quote(entry.Note)})";
    }

    // Reports, without a fix, a legacy tagging whose replacement would
    // overwrite a tag: one selector per guarded key. The guarded key condition
    // follows the match conditions in every selector, so the message names it
    // by its index.
    private MapCssRule BuildGuardRule(TagReplacement entry, int index)
    {
        var selectors = entry.GuardedSets
            .Select(tag => $"[{Quote(tag.Key)}][{Quote(tag.Key)}!~/^(?:{Alternation(table.AcceptedValues(tag.Key, tag.Value).Order(StringComparer.Ordinal))})$/]")
            .Concat(entry.GuardedTargets.Select(key => $"[{Quote(key)}]"))
            .Select(condition => MatchSelector(entry) + condition)
            .ToList();

        return new MapCssRule(
            index == 0 ? "Legacy tagging whose replacement would overwrite a tag, fixed by hand" : null,
            selectors,
            [
                $"throwError: tr(\"{{0}} is deprecated, but {{1}} is already set: fix it by hand\", {Quote(RuleTable.Describe(entry.PresentTags))}, \"{{{entry.Match.Count}.key}}\")",
                .. GuardSamples(entry).Select(sample => $"assertMatch: {Quote(AssertNode([.. SampleTags(entry), sample]))}"),
                $"assertNoMatch: {Quote(AssertNode(SampleTags(entry)))}",
            ]);
    }

    private static string MatchSelector(TagReplacement entry) =>
        "node" + string.Concat(entry.Match.Select(tag => tag.Value switch
        {
            null => $"[!{Quote(tag.Key)}]",
            RuleTable.AnyValue => $"[{Quote(tag.Key)}]",
            _ => $"[{Quote(tag.Key)}={Quote(tag.Value)}]",
        }));

    // The matched tags of a self-test node, with a sample for "any value".
    private static IEnumerable<KeyValuePair<string, string>> SampleTags(TagReplacement entry) =>
        entry.PresentTags.Select(tag => tag.Value == RuleTable.AnyValue ? KeyValuePair.Create(tag.Key, "yes") : tag);

    // One tag per guard that blocks the fix.
    private static IEnumerable<KeyValuePair<string, string>> GuardSamples(TagReplacement entry) =>
        entry.GuardedSets.Select(tag => tag.Key).Concat(entry.GuardedTargets)
            .Select(key => KeyValuePair.Create(key, ConflictingValue));

    private MapCssRule BuildConflictRule(TagConflict entry, int index)
    {
        var selector = "node" + string.Concat(entry.Tags.Select(tag => $"[{Quote(tag.Key)}={Quote(tag.Value)}]"));
        var tags = RuleTable.Describe(entry.Tags);
        var message = string.IsNullOrWhiteSpace(entry.Note)
            ? $"throwError: tr(\"{{0}} cannot be combined\", {Quote(tags)})"
            : $"throwError: tr(\"{{0}} cannot be combined: {{1}}\", {Quote(tags)}, {Quote(entry.Note)})";

        return new MapCssRule(
            index == 0 ? "Documented alternatives that cannot be combined" : null,
            [selector],
            [
                message,
                $"assertMatch: {Quote(AssertNode(entry.Tags))}",
                .. entry.Tags.Select(tag => $"assertNoMatch: {Quote(AssertNode([tag]))}"),
            ]);
    }

    // A documented tag implied by the rest of the tagging: a warning, not an
    // error, with a fix that removes it. The self-tests check that the rule
    // needs the implying tag, and that a key required absent prevents it.
    private MapCssRule BuildRedundancyRule(TagReplacement entry, int index)
    {
        var implicitTags = Quote(RuleTable.Describe(entry.PresentTags.Where(tag => entry.Remove.Contains(tag.Key))));
        var message = string.IsNullOrWhiteSpace(entry.Note)
            ? $"throwWarning: tr(\"{{0}} is implicit\", {implicitTags})"
            : $"throwWarning: tr(\"{{0}} is implicit: {{1}}\", {implicitTags}, {Quote(entry.Note)})";

        var sample = SampleTags(entry).ToList();
        var implying = sample.Where(tag => !entry.Remove.Contains(tag.Key)).ToList();
        var blockers = entry.Match.Where(tag => tag.Value is null)
            .Select(tag => KeyValuePair.Create(tag.Key, ConflictingValue));

        return new MapCssRule(
            index == 0 ? "Documented tags implied by the rest of the tagging" : null,
            [MatchSelector(entry)],
            [
                message,
                .. entry.Remove.Select(key => $"fixRemove: {Quote(key)}"),
                $"assertMatch: {Quote(AssertNode(sample))}",
                $"assertNoMatch: {Quote(AssertNode(implying))}",
                .. blockers.Select(blocker => $"assertNoMatch: {Quote(AssertNode([.. sample, blocker]))}"),
            ]);
    }

    private MapCssRule BuildUndocumentedKeyRule(KeyValuePair<string, string> scope)
    {
        var accepted = schema.Keys.Select(spec => spec.Key).Concat(LegacyKeys()).Distinct().Order(StringComparer.Ordinal);
        var suffixes = accepted.Select(key => key[settings.KeyPrefix.Length..]);
        var selector = $"node[/^{EscapeRegex(settings.KeyPrefix)}(?!(?:{Alternation(suffixes)})$)/].{_scopeClass}";

        var undocumented = KeyValuePair.Create(scope.Key + UndocumentedSuffix, "yes");
        var reference = KeyValuePair.Create(scope.Key + ":ref", "123456");
        var foreign = KeyValuePair.Create(scope.Key, "XX:test");

        return new MapCssRule(
            "Keys that the wiki does not document",
            [selector],
            [
                $"throwError: tr(\"{{0}} is not documented in the {{1}}\", \"{{0.key}}\", {Quote(settings.SchemeName)})",
                $"assertMatch: {Quote(AssertNode([scope, undocumented]))}",
                $"assertNoMatch: {Quote(AssertNode([scope]))}",
                $"assertNoMatch: {Quote(AssertNode([scope, reference]))}",
                $"assertNoMatch: {Quote(AssertNode([foreign, undocumented]))}",
            ]);
    }

    private MapCssRule BuildUndocumentedValueRule(KeyValuePair<string, string> scope)
    {
        var legacyValues = LegacyValues();
        var closedKeys = schema.Keys.Where(spec => !spec.IsOpen).ToList();
        var foreign = $"(?!{CaseInsensitive(settings.CountryPrefix)}:){ForeignPrefixPattern}";

        var selectors = closedKeys.Select(spec =>
        {
            var accepted = spec.Values.Where(value => !schema.IsForeign(value))
                .Concat(legacyValues.GetValueOrDefault(spec.Key, NoValues))
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList();

            // A key documented with foreign values only accepts no domestic
            // value: no empty alternative in front of the foreign pattern.
            var alternatives = accepted.Count > 0 ? $"{Alternation(accepted)}|{foreign}" : foreign;

            // The presence test comes first: Osmose-QA matches a regular
            // expression against an empty string when the key is absent.
            var key = Quote(spec.Key);
            return $"node[{key}][{key}=~/(^|;)(?!(?:{alternatives})(?:;|$))/].{_scopeClass}";
        }).ToList();

        List<string> declarations =
        [
            $"throwError: tr(\"{{0}} has a value that is not documented in the {{1}} (values are case-sensitive)\", \"{{0.key}}\", {Quote(settings.SchemeName)})",
        ];

        foreach (var spec in SampleKeys(closedKeys))
        {
            KeyValuePair<string, string>[] context = spec.Key == scope.Key ? [] : [scope];
            var documented = string.Join(TagSchema.ValueSeparator, spec.Values);
            var mangled = MangleValue(spec, legacyValues.GetValueOrDefault(spec.Key, NoValues));
            declarations.Add($"assertMatch: {Quote(AssertNode([.. context, KeyValuePair.Create(spec.Key, mangled)]))}");
            declarations.Add($"assertNoMatch: {Quote(AssertNode([.. context, KeyValuePair.Create(spec.Key, documented)]))}");
            declarations.Add($"assertNoMatch: {Quote(AssertNode([.. context, KeyValuePair.Create(spec.Key, "XX:test")]))}");
        }

        return new MapCssRule("Values that the wiki does not list", selectors, declarations);
    }

    // The first documented signal type tag with the country prefix, used as the
    // context of the self-tests that need a node in scope.
    private KeyValuePair<string, string> FindScopeSample()
    {
        foreach (var spec in schema.Keys)
        {
            var value = spec.Values.FirstOrDefault(schema.IsDomestic);
            if (value is not null)
            {
                return KeyValuePair.Create(spec.Key, value);
            }
        }

        throw new InvalidOperationException($"The wiki documents no {settings.CountryPrefix}: value under {settings.KeyPrefix}.");
    }

    // Every list-valued key (states) plus a few other closed keys, all with a
    // value that has the country prefix.
    private IEnumerable<KeySpec> SampleKeys(IReadOnlyList<KeySpec> closedKeys)
    {
        var domestic = closedKeys.Where(spec => spec.Values.Any(schema.IsDomestic)).ToList();
        return domestic.Where(IsStatesKey).Concat(domestic.Where(spec => !IsStatesKey(spec)).Take(ValueSampleCount));
    }

    private static bool IsStatesKey(KeySpec spec) => spec.Key.EndsWith(":states", StringComparison.Ordinal);

    // A value that differs from a documented one only by its case, which is the
    // typical mistake (FR:m for FR:M); a suffix is used when the case cannot
    // make it undocumented.
    private string MangleValue(KeySpec spec, IReadOnlySet<string> legacy)
    {
        var value = spec.Values.First(schema.IsDomestic);
        var separator = value.IndexOf(':') + 1;
        var head = value[..separator];
        var tail = value[separator..];

        string[] candidates = [head + tail.ToLowerInvariant(), head + tail.ToUpperInvariant(), value + UndocumentedSuffix];
        return candidates.First(candidate => !spec.Values.Contains(candidate) && !legacy.Contains(candidate));
    }

    // Undocumented keys reported by a tag replacement rule.
    private IEnumerable<string> LegacyKeys() =>
        table.TagReplacements
            .Where(entry => entry.IsUnconditional)
            .SelectMany(entry => entry.PresentTags.Select(tag => tag.Key).Concat(entry.Rename.Keys))
            .Where(key => key.StartsWith(settings.KeyPrefix, StringComparison.Ordinal) && !schema.IsDocumentedKey(key));

    // Undocumented values reported by a replacement rule, by key.
    private Dictionary<string, IReadOnlySet<string>> LegacyValues() =>
        table.ValueReplacements.Select(entry => KeyValuePair.Create(entry.Key, entry.From))
            .Concat(table.TagReplacements.Where(entry => entry.IsUnconditional).SelectMany(entry => entry.PresentTags))
            .Where(tag => tag.Value != RuleTable.AnyValue && !schema.IsDocumentedValue(tag.Key, tag.Value))
            .GroupBy(tag => tag.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)group.Select(tag => tag.Value).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

    // Matches the literal as a whole token of a ";" separated value.
    private static string TokenPattern(string escapedLiteral) => $"(^|;){escapedLiteral}(;|$)";
}
