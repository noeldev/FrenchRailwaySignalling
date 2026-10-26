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
//    prefix in one of the checked keys); every other rule is restricted to it;
// 2. one rule per legacy value and per legacy tagging, with its fix. The
//    message only names the legacy tags, to keep the JOSM dialogs narrow; the
//    replacement is carried by the fix. The fix of a legacy tagging is guarded
//    so it never overwrites a tag it does not match; one more rule reports,
//    without a fix, the legacy tagging a guard keeps. A legacy tagging with a
//    note and nothing to fix is only reported;
// 3. one rule per conflict between documented alternatives, and one warning
//    per redundant tag, with a fix that removes it;
// 4. one rule per closed key for the mistyped values (see
//    MistypedValueRuleBuilder);
// 5. rules for keys the wiki does not document, with a fix that removes them;
// 6. one rule for values the wiki does not list, for every closed key. Values
//    of another scheme (CH-FDV:, ETCS:...) are out of scope and never reported.
// The rules of 2 and 4 mark the keys they deal with (RuleScope), and 5 and 6
// skip a marked key, so a tag is reported once, by the rule that fixes it.
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

    public IReadOnlyList<MapCssRule> Build()
    {
        var scope = new RuleScope(settings, FindScopeSamples());
        var mistyped = new MistypedValueRuleBuilder(schema, table, settings, scope).Build();
        return
        [
            BuildScopeRule(scope),
            .. table.ValueReplacements.Select((entry, index) => BuildValueReplacementRule(entry, index, scope, table)),
            .. table.TagReplacements.Select((entry, index) => BuildTagReplacementRule(entry, index, scope)),
            .. table.TagReplacements.Where(entry => !entry.IsReportOnly && (entry.GuardedSets.Any() || entry.GuardedTargets.Any()))
                .Select((entry, index) => BuildGuardRule(entry, index, scope)),
            .. table.Conflicts.Select((entry, index) => BuildConflictRule(entry, index, scope)),
            .. table.Redundancies.Select((entry, index) => BuildRedundancyRule(entry, index, scope)),
            .. mistyped,
            BuildUndocumentedKeyRule(scope),
            .. BuildHandledUndocumentedKeyRules(scope),
            BuildUndocumentedValueRule(scope),
        ];
    }

    private MapCssRule BuildScopeRule(RuleScope scope) =>
        new(
            $"Scope: nodes with a {settings.CountryPrefix} value in a {settings.KeyPrefix}* key",
            [scope.Selector],
            [$"set .{scope.ClassName}"]);

    // One rule per legacy token, so the messages name it; the fix renames every
    // legacy token of the key at once (FR:Rc;FR:Ac), so one fix is enough.
    private static MapCssRule BuildValueReplacementRule(ValueReplacement entry, int index, RuleScope scope, RuleTable table)
    {
        var selector = scope.Restrict($"node[{Quote(entry.Key)}=~/{TokenPattern(EscapeRegex(entry.From))}/]");
        var replacements = table.ValueReplacements.Where(replacement => replacement.Key == entry.Key);
        var fix = $"concat({Quote(entry.Key + "=")}, {Respelled(entry.Key, replacements)})";

        List<string> declarations =
        [
            scope.SetHandled(entry.Key),
            $"throwError: tr(\"{{0}}={{1}} is deprecated\", {Quote(entry.Key)}, {Quote(entry.From)})",
            $"fixAdd: {fix}",
            $"assertMatch: {Quote(AssertNode(scope.InScope([KeyValuePair.Create(entry.Key, entry.From)])))}",
        ];

        // An empty replacement drops the token: no replacement value to test.
        if (entry.To.Length > 0)
        {
            declarations.Add($"assertNoMatch: {Quote(AssertNode(scope.InScope([KeyValuePair.Create(entry.Key, entry.To)])))}");
        }

        return new MapCssRule(index == 0 ? "Legacy values, renamed in place" : null, [selector], declarations);
    }

    private MapCssRule BuildTagReplacementRule(TagReplacement entry, int index, RuleScope scope)
    {
        // A set tag must be absent (an empty value for Osmose-QA) or hold an
        // accepted value; a rename target must be absent.
        var guards = entry.GuardedSets
            .Select(tag => $"[{Quote(tag.Key)}!~/^(?!(?:{Alternation(table.AcceptedValues(tag.Key, tag.Value).Order(StringComparer.Ordinal))}|)$)/]")
            .Concat(entry.GuardedTargets.Select(key => $"[!{Quote(key)}]"));

        List<string> declarations =
        [
            .. entry.HandledKeys.Select(scope.SetHandled),
            .. IsSpecialization(entry) ? [$"set .{RuleClass(entry)}"] : Array.Empty<string>(),
            DeprecatedMessage(entry),
            .. FixDeclarations(entry, table),
            $"assertMatch: {Quote(AssertNode(scope.InScope(SampleTags(entry))))}",
        ];

        if (entry.Set.Count > 0)
        {
            declarations.Add($"assertNoMatch: {Quote(AssertNode(scope.InScope(entry.Set)))}");
        }

        if (!entry.IsReportOnly)
        {
            declarations.AddRange(GuardSamples(entry).Select(sample => $"assertNoMatch: {Quote(AssertNode(scope.InScope([.. SampleTags(entry), sample])))}"));
        }

        var selector = MatchSelector(entry) + (entry.IsReportOnly ? string.Empty : string.Concat(guards)) + Exclusions(entry);
        return new MapCssRule(
            index == 0 ? "Legacy tagging, restructured" : null,
            [scope.Restrict(selector)],
            declarations);
    }

    // The fix commands in the order JOSM runs them: renames, removals, tags
    // set, then the defaults. A renamed value is respelled by the value
    // replacements of its new key (a sign form moved to a key that only
    // documents board becomes board), so the fix does not leave a legacy value
    // behind. A renamed key with a default takes the value of the old key when
    // there is one, else the default; any other default only fills a key left
    // absent.
    private static IEnumerable<string> FixDeclarations(TagReplacement entry, RuleTable table)
    {
        foreach (var (from, to) in entry.Rename)
        {
            var respellings = table.ValueReplacements.Where(replacement => replacement.Key == to).ToList();
            var hasDefault = entry.Default.TryGetValue(to, out var fallback);
            if (respellings.Count == 0 && !hasDefault)
            {
                yield return $"fixChangeKey: {Quote($"{from}=>{to}")}";
                continue;
            }

            var value = respellings.Count > 0 ? Respelled(from, respellings) : $"tag({Quote(from)})";
            var otherwise = hasDefault ? Quote(fallback!) : $"tag({Quote(to)})";
            yield return $"fixAdd: concat({Quote(to + "=")}, cond(has_tag_key({Quote(from)}), {value}, {otherwise}))";
            yield return $"fixRemove: {Quote(from)}";
        }

        foreach (var key in entry.Remove)
        {
            yield return $"fixRemove: {Quote(key)}";
        }

        foreach (var (key, value) in entry.Set)
        {
            yield return $"fixAdd: {Quote($"{key}={value}")}";
        }

        foreach (var (key, value) in entry.Default.Where(tag => !entry.Rename.ContainsValue(tag.Key)))
        {
            yield return $"fixAdd: concat({Quote(key + "=")}, any(tag({Quote(key)}), {Quote(value)}))";
        }
    }

    // The value of a key with value replacements applied: each token is
    // replaced inside ";list;", then the empty entries are dropped. This works
    // for a single value and for a list alike, and drops a token whose
    // replacement is empty (JOSM removes a tag set to "").
    private static string Respelled(string key, IEnumerable<ValueReplacement> replacements)
    {
        var separator = TagSchema.ValueSeparator.ToString();
        var expression = $"concat({Quote(separator)}, tag({Quote(key)}), {Quote(separator)})";
        foreach (var replacement in replacements)
        {
            expression = $"replace({expression}, {Quote(separator + replacement.From + separator)}, {Quote(separator + replacement.To + separator)})";
        }

        return $"join_list({Quote(separator)}, trim_list(split({Quote(separator)}, {expression})))";
    }

    // A legacy tagging that is only reported names it with the note, which
    // tells what to do. Otherwise it is deprecated, or not documented for a
    // mistake, followed by the note, or by the question of a suggestion
    // (whether the tags of its fix were meant).
    private static string DeprecatedMessage(TagReplacement entry)
    {
        var legacy = Quote(RuleTable.Describe(entry.LegacyTags));
        if (entry.IsReportOnly)
        {
            return $"throwError: tr(\"{{0}}: {{1}}\", {legacy}, {Quote(entry.Note!)})";
        }

        var status = entry.Undocumented ? "is not documented" : "is deprecated";
        if (entry.Suggest)
        {
            var question = string.IsNullOrWhiteSpace(entry.Note) ? RuleTable.Describe(entry.Set) : entry.Note;
            return $"throwError: tr(\"{{0}} {status}: did you mean {{1}}?\", {legacy}, {Quote(question)})";
        }

        return string.IsNullOrWhiteSpace(entry.Note)
            ? $"throwError: tr(\"{{0}} {status}\", {legacy})"
            : $"throwError: tr(\"{{0}} {status}: {{1}}\", {legacy}, {Quote(entry.Note)})";
    }

    // Reports, without a fix, a legacy tagging whose replacement would
    // overwrite a tag: one selector per guarded key. The guarded key condition
    // follows the match conditions in every selector, so the message names it
    // by its index. A guarded key that another legacy rule deals with is left
    // to that rule: once fixed, the replacement may no longer be blocked.
    private MapCssRule BuildGuardRule(TagReplacement entry, int index, RuleScope scope)
    {
        var selectors = entry.GuardedSets
            .Select(tag => (tag.Key, Condition: $"[{Quote(tag.Key)}][{Quote(tag.Key)}!~/^(?:{Alternation(table.AcceptedValues(tag.Key, tag.Value).Order(StringComparer.Ordinal))})$/]"))
            .Concat(entry.GuardedTargets.Select(key => (Key: key, Condition: $"[{Quote(key)}]")))
            .Select(guard => scope.Restrict($"{MatchSelector(entry)}{guard.Condition}!.{scope.HandledClass(guard.Key)}{Exclusions(entry)}"))
            .ToList();

        return new MapCssRule(
            index == 0 ? "Legacy tagging whose replacement would overwrite a tag, fixed by hand" : null,
            selectors,
            [
                .. entry.HandledKeys.Select(scope.SetHandled),
                $"throwError: tr(\"{{0}} is deprecated, but {{1}} is already set: fix it by hand\", {Quote(RuleTable.Describe(entry.LegacyTags))}, \"{{{entry.Match.Count}.key}}\")",
                .. GuardSamples(entry).Select(sample => $"assertMatch: {Quote(AssertNode(scope.InScope([.. SampleTags(entry), sample])))}"),
                $"assertNoMatch: {Quote(AssertNode(scope.InScope(SampleTags(entry))))}",
            ]);
    }

    // A replacement whose match extends the match of another one (the same
    // legacy value with a known caption...) is more specific: it comes first
    // in the table, and the general one is skipped where it matched.
    private IEnumerable<TagReplacement> Specializations(TagReplacement entry) =>
        table.TagReplacements.Where(other => other != entry
            && other.Match.Count > entry.Match.Count
            && entry.Match.All(tag => other.Match.TryGetValue(tag.Key, out var value) && value == tag.Value));

    private bool IsSpecialization(TagReplacement entry) =>
        table.TagReplacements.Any(other => Specializations(other).Contains(entry));

    private string Exclusions(TagReplacement entry) =>
        string.Concat(Specializations(entry).Select(other => $"!.{RuleClass(other)}"));

    private string RuleClass(TagReplacement entry) => $"legacy_rule_{table.TagReplacements.IndexOf(entry)}";

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

    private static MapCssRule BuildConflictRule(TagConflict entry, int index, RuleScope scope)
    {
        var selector = scope.Restrict("node" + string.Concat(entry.Tags.Select(tag => $"[{Quote(tag.Key)}={Quote(tag.Value)}]")));
        var tags = RuleTable.Describe(entry.Tags);
        var message = string.IsNullOrWhiteSpace(entry.Note)
            ? $"throwError: tr(\"{{0}} cannot be combined\", {Quote(tags)})"
            : $"throwError: tr(\"{{0}} cannot be combined: {{1}}\", {Quote(tags)}, {Quote(entry.Note)})";

        return new MapCssRule(
            index == 0 ? "Documented alternatives that cannot be combined" : null,
            [selector],
            [
                message,
                $"assertMatch: {Quote(AssertNode(scope.InScope(entry.Tags)))}",
                .. entry.Tags.Select(tag => $"assertNoMatch: {Quote(AssertNode(scope.InScope([tag])))}"),
            ]);
    }

    // A documented tag implied by the rest of the tagging: a warning, not an
    // error, with a fix that removes it. The self-tests check that the rule
    // needs the implying tag, and that a key required absent prevents it.
    private static MapCssRule BuildRedundancyRule(TagReplacement entry, int index, RuleScope scope)
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
            [scope.Restrict(MatchSelector(entry))],
            [
                message,
                .. entry.Remove.Select(key => $"fixRemove: {Quote(key)}"),
                $"assertMatch: {Quote(AssertNode(scope.InScope(sample)))}",
                $"assertNoMatch: {Quote(AssertNode(scope.InScope(implying)))}",
                .. blockers.Select(blocker => $"assertNoMatch: {Quote(AssertNode(scope.InScope([.. sample, blocker])))}"),
            ]);
    }

    // The keys nobody deals with. The fix removes the key, one per run: the
    // next one is reported after it. JOSM matches the selector again, without
    // the classes, to spell the key of the fix, so the selector is scoped by
    // its tag condition rather than by the scope class. The accepted keys
    // (documented elsewhere) are never reported.
    private MapCssRule BuildUndocumentedKeyRule(RuleScope scope)
    {
        var accepted = schema.Keys.Select(spec => spec.Key).Concat(HandledUndocumentedKeys()).Concat(table.AcceptedKeys)
            .Distinct().Order(StringComparer.Ordinal);
        var suffixes = accepted.Select(key => key[settings.KeyPrefix.Length..]);
        var selector = $"node[/^{EscapeRegex(settings.KeyPrefix)}(?!(?:{Alternation(suffixes)})$)/]{scope.Condition}";

        var undocumented = KeyValuePair.Create(scope.Sample.Key + UndocumentedSuffix, "yes");
        var reference = KeyValuePair.Create(scope.Sample.Key + ":ref", "123456");
        var foreign = KeyValuePair.Create(scope.Sample.Key, "XX:test");

        return new MapCssRule(
            "Keys that the wiki does not document",
            [selector],
            [
                UndocumentedKeyMessage("\"{0.key}\""),
                "fixRemove: \"{0.key}\"",
                $"assertMatch: {Quote(AssertNode([scope.Sample, undocumented]))}",
                $"assertNoMatch: {Quote(AssertNode([scope.Sample]))}",
                $"assertNoMatch: {Quote(AssertNode([scope.Sample, reference]))}",
                $"assertNoMatch: {Quote(AssertNode([foreign, undocumented]))}",
            ]);
    }

    // The undocumented keys that a legacy rule deals with, on the nodes where
    // no such rule matched: one rule per key, so the fix names its key.
    private IEnumerable<MapCssRule> BuildHandledUndocumentedKeyRules(RuleScope scope) =>
        HandledUndocumentedKeys().Select((key, index) => new MapCssRule(
            index == 0 ? "Undocumented keys that a legacy rule deals with, where it did not match" : null,
            [scope.Restrict($"node[{Quote(key)}]!.{scope.HandledClass(key)}")],
            [UndocumentedKeyMessage(Quote(key)), $"fixRemove: {Quote(key)}"]));

    private string UndocumentedKeyMessage(string key) =>
        $"throwError: tr(\"{{0}} is not documented in the {{1}}\", {key}, {Quote(settings.SchemeName)})";

    private MapCssRule BuildUndocumentedValueRule(RuleScope scope)
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
            return scope.Restrict($"node[{key}][{key}=~/(^|;)(?!(?:{alternatives})(?:;|$))/]!.{scope.HandledClass(spec.Key)}");
        }).ToList();

        List<string> declarations =
        [
            $"throwError: tr(\"{{0}} has a value that is not documented in the {{1}}\", \"{{0.key}}\", {Quote(settings.SchemeName)})",
        ];

        foreach (var spec in SampleKeys(closedKeys))
        {
            KeyValuePair<string, string>[] context = spec.Key == scope.Sample.Key ? [] : [scope.Sample];
            var documented = string.Join(TagSchema.ValueSeparator, spec.Values);
            var undocumented = spec.Values.First(schema.IsDomestic) + UndocumentedSuffix;
            declarations.Add($"assertMatch: {Quote(AssertNode([.. context, KeyValuePair.Create(spec.Key, undocumented)]))}");
            declarations.Add($"assertNoMatch: {Quote(AssertNode([.. context, KeyValuePair.Create(spec.Key, documented)]))}");
            declarations.Add($"assertNoMatch: {Quote(AssertNode([.. context, KeyValuePair.Create(spec.Key, "XX:test")]))}");
        }

        return new MapCssRule("Values that the wiki does not list", selectors, declarations);
    }

    // The first two documented tags with the country prefix, of two keys, used
    // as the context of the self-tests that need a node in scope.
    private List<KeyValuePair<string, string>> FindScopeSamples()
    {
        List<KeyValuePair<string, string>> samples =
        [
            .. schema.Keys
                .Select(spec => KeyValuePair.Create(spec.Key, spec.Values.FirstOrDefault(schema.IsDomestic)))
                .Where(sample => sample.Value is not null)
                .Select(sample => KeyValuePair.Create(sample.Key, sample.Value!))
                .Take(2),
        ];

        return samples.Count == 2
            ? samples
            : throw new InvalidOperationException($"The wiki documents fewer than two keys with a {settings.CountryPrefix}: value under {settings.KeyPrefix}.");
    }

    // Every list-valued key (states) plus a few other closed keys, all with a
    // value that has the country prefix.
    private IEnumerable<KeySpec> SampleKeys(IReadOnlyList<KeySpec> closedKeys)
    {
        var domestic = closedKeys.Where(spec => spec.Values.Any(schema.IsDomestic)).ToList();
        return domestic.Where(IsStatesKey).Concat(domestic.Where(spec => !IsStatesKey(spec)).Take(ValueSampleCount));
    }

    private static bool IsStatesKey(KeySpec spec) => spec.Key.EndsWith(":states", StringComparison.Ordinal);

    // Undocumented keys under the checked prefix that a legacy rule deals with.
    private List<string> HandledUndocumentedKeys() =>
    [
        .. table.TagReplacements.SelectMany(entry => entry.HandledKeys)
            .Where(key => key.StartsWith(settings.KeyPrefix, StringComparison.Ordinal) && !schema.IsDocumentedKey(key))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

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
