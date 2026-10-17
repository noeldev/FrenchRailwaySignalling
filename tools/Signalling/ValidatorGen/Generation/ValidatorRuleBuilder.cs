// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using ValidatorGen.Core;
using ValidatorGen.MapCss;
using ValidatorGen.Rules;
using ValidatorGen.Schema;
using static ValidatorGen.MapCss.MapCssSyntax;

namespace ValidatorGen.Generation;

// Turns the documented scheme and the rule table into MapCSS rules:
// 1. a scope rule that marks the nodes to check (a value with the country
//    prefix in one of the checked keys);
// 2. one rule per legacy value and per legacy tagging, with its fix. The
//    message only names the legacy tagging, to keep the JOSM dialogs narrow;
//    the replacement is carried by the fix;
// 3. one rule per conflict between documented alternatives;
// 4. one rule for keys the wiki does not document;
// 5. one rule for values the wiki does not list, for every closed key. Values
//    of another scheme (CH-FDV:, ETCS:...) are out of scope and never reported.
// Legacy keys and values reported by 2 are left out of 4 and 5, so a legacy tag
// is reported once, with its fix.
//
// The assertMatch/assertNoMatch declarations are self-tests run by JOSM and
// Osmose-QA against the rule itself; they never inspect map data.
public sealed class ValidatorRuleBuilder(TagSchema schema, RuleTable table, ValidatorSettings settings)
{
    private const int ValueSampleCount = 2;
    private const string UndocumentedSuffix = "_undocumented";

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
            .. table.Conflicts.Select(BuildConflictRule),
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

        // Replace the token inside ";list;" then drop the empty edge entries,
        // which works for a single value and for a list alike.
        var listed = $"concat({Quote(separator)}, tag({Quote(entry.Key)}), {Quote(separator)})";
        var replaced = $"replace({listed}, {Quote(separator + entry.From + separator)}, {Quote(separator + entry.To + separator)})";
        var fix = $"concat({Quote(entry.Key + "=")}, join_list({Quote(separator)}, trim_list(split({Quote(separator)}, {replaced}))))";

        return new MapCssRule(
            index == 0 ? "Legacy values, renamed in place" : null,
            [selector],
            [
                $"throwError: tr(\"{{0}}={{1}} is deprecated\", {Quote(entry.Key)}, {Quote(entry.From)})",
                $"fixAdd: {fix}",
                $"assertMatch: {Quote(AssertNode([KeyValuePair.Create(entry.Key, entry.From)]))}",
                $"assertNoMatch: {Quote(AssertNode([KeyValuePair.Create(entry.Key, entry.To)]))}",
            ]);
    }

    private MapCssRule BuildTagReplacementRule(TagReplacement entry, int index)
    {
        var selector = "node" + string.Concat(entry.Match.Select(tag => tag.Value is null
            ? $"[!{Quote(tag.Key)}]"
            : $"[{Quote(tag.Key)}={Quote(tag.Value)}]"));

        List<string> declarations =
        [
            $"throwError: tr(\"{{0}} is deprecated\", {Quote(RuleTableValidator.Describe(entry.PresentTags))})",
            .. entry.Rename.Select(rename => $"fixChangeKey: {Quote($"{rename.Key}=>{rename.Value}")}"),
            .. entry.Remove.Select(key => $"fixRemove: {Quote(key)}"),
            .. entry.Set.Select(tag => $"fixAdd: {Quote($"{tag.Key}={tag.Value}")}"),
            $"assertMatch: {Quote(AssertNode(entry.PresentTags))}",
        ];

        if (entry.Set.Count > 0)
        {
            declarations.Add($"assertNoMatch: {Quote(AssertNode(entry.Set))}");
        }

        return new MapCssRule(index == 0 ? "Legacy tagging, restructured" : null, [selector], declarations);
    }

    private MapCssRule BuildConflictRule(TagConflict entry, int index)
    {
        var selector = "node" + string.Concat(entry.Tags.Select(tag => $"[{Quote(tag.Key)}={Quote(tag.Value)}]"));
        var tags = RuleTableValidator.Describe(entry.Tags);
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
            .SelectMany(entry => entry.PresentTags.Select(tag => tag.Key).Concat(entry.Rename.Keys))
            .Where(key => key.StartsWith(settings.KeyPrefix, StringComparison.Ordinal) && !schema.IsDocumentedKey(key));

    // Undocumented values reported by a replacement rule, by key.
    private Dictionary<string, IReadOnlySet<string>> LegacyValues() =>
        table.ValueReplacements.Select(entry => KeyValuePair.Create(entry.Key, entry.From))
            .Concat(table.TagReplacements.SelectMany(entry => entry.PresentTags))
            .Where(tag => !schema.IsDocumentedValue(tag.Key, tag.Value))
            .GroupBy(tag => tag.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlySet<string>)group.Select(tag => tag.Value).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

    // Matches the literal as a whole token of a ";" separated value.
    private static string TokenPattern(string escapedLiteral) => $"(^|;){escapedLiteral}(;|$)";
}
