// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Rules;
using Signalling.Schema;
using ValidatorGen.Core;
using ValidatorGen.MapCss;
using static ValidatorGen.MapCss.MapCssSyntax;

namespace ValidatorGen.Generation;

// One rule per closed key for the typing mistakes that have a single, certain
// correction, with a fix. MapCSS cannot look values up, so the corrections are
// spelled out from the documented values:
// - the case of a token (FR:a -> FR:A, Backward -> backward): the all-lower,
//   all-upper and capitalized spellings only, alone or with a missing country
//   prefix (S -> FR:S);
// - for keys with country values: a doubled prefix (FR:FR:S), a wrong or
//   missing separator after it (FR=A, FR(R)+A), a leading "=" (=FR:ID1), a
//   colon used as list separator (FR:C:FR:S);
// - empty entries and edge spaces.
// A spelling that could stand for two documented values, or that is itself
// accepted, is left out. The fix trims the entries, applies the generic
// repairs, then respells the tokens; what it cannot repair is reported by the
// rule for undocumented values once fixed. The same mistakes are corrected by
// the MapRoulette challenge (TokenNormalizer), which can compare against the
// documented values directly.
public sealed class MistypedValueRuleBuilder(TagSchema schema, RuleTable table, ValidatorSettings settings, RuleScope scope)
{
    private const string Separator = ";";

    public IReadOnlyList<MapCssRule> Build()
    {
        List<MapCssRule> rules = [];
        foreach (var spec in schema.Keys.Where(spec => !spec.IsOpen))
        {
            var variants = Variants(spec);
            var domestic = spec.Values.Any(schema.IsDomestic);
            if (variants.Count == 0 && !domestic)
            {
                continue;
            }

            rules.Add(BuildRule(spec, variants, domestic, rules.Count == 0));
        }

        return rules;
    }

    private MapCssRule BuildRule(KeySpec spec, IReadOnlyList<KeyValuePair<string, string>> variants, bool domestic, bool first)
    {
        var key = Quote(spec.Key);
        List<string> patterns = [];
        if (variants.Count > 0)
        {
            patterns.Add($"(^|;)(?:{Alternation(variants.Select(variant => variant.Key))})(;|$)");
        }

        if (domestic)
        {
            var country = EscapeRegex(settings.CountryPrefix);
            patterns.Add($"{country}:{country}:");
            patterns.Add($"(^|;)={country}:");
            patterns.Add($"(^|;){country}\\(");
            patterns.Add($"(^|;){country}=");
            patterns.Add($"[^;]:{country}:");
        }

        patterns.Add("(^|;)(;|$)");
        patterns.Add("(^|;)\\s|\\s(;|$)");

        var selector = scope.Restrict($"node[{key}][{key}=~/{string.Join('|', patterns)}/]");

        // Trimmed entries without the empty ones, framed by separators so a
        // token is replaced whole: ";FR:a;FR:c;".
        var expression = $"concat({Quote(Separator)}, join_list({Quote(Separator)}, trim_list(split({Quote(Separator)}, tag({key})))), {Quote(Separator)})";
        if (domestic)
        {
            var prefix = settings.CountryPrefix + ":";
            expression = Replace(expression, prefix + prefix, prefix);
            expression = Replace(expression, Separator + "=", Separator);
            expression = Replace(expression, Separator + settings.CountryPrefix + "(", Separator + prefix + "(");
            expression = Replace(expression, Separator + settings.CountryPrefix + "=", Separator + prefix);
            expression = Replace(expression, ":" + prefix, Separator + prefix);
        }

        foreach (var (variant, value) in variants)
        {
            expression = Replace(expression, Separator + variant + Separator, Separator + value + Separator);
        }

        var fix = $"concat({Quote(spec.Key + "=")}, join_list({Quote(Separator)}, trim_list(split({Quote(Separator)}, {expression}))))";

        // A documented value, and a mistyped one: a respelling, else the
        // documented value with its prefix doubled.
        var documented = spec.Values.FirstOrDefault(schema.IsDomestic) ?? spec.Values.First(value => !schema.IsForeign(value));
        var sample = variants.Count > 0 ? variants[0].Key : settings.CountryPrefix + ":" + documented;

        return new MapCssRule(
            first ? "Mistyped values, with their single correction" : null,
            [selector],
            [
                scope.SetHandled(spec.Key),
                $"throwError: tr(\"{{0}}={{1}} is mistyped\", \"{{0.key}}\", \"{{0.value}}\")",
                $"fixAdd: {fix}",
                $"assertMatch: {Quote(AssertNode(scope.InScope([KeyValuePair.Create(spec.Key, sample)])))}",
                $"assertNoMatch: {Quote(AssertNode(scope.InScope([KeyValuePair.Create(spec.Key, documented)])))}",
            ]);
    }

    private static string Replace(string expression, string from, string to) =>
        $"replace({expression}, {Quote(from)}, {Quote(to)})";

    // The certain respellings of the documented values of a key: spelling ->
    // documented value. A spelling that is accepted (documented or legacy) or
    // that stands for two documented values is left out.
    private List<KeyValuePair<string, string>> Variants(KeySpec spec)
    {
        var accepted = spec.Values.Concat(LegacyValues(spec.Key)).ToHashSet(StringComparer.Ordinal);
        var prefix = settings.CountryPrefix + ":";

        return
        [
            .. spec.Values.Where(value => !schema.IsForeign(value))
                .SelectMany(value => Spellings(value, prefix).Select(spelling => KeyValuePair.Create(spelling, value)))
                .Where(variant => !accepted.Contains(variant.Key) && !schema.IsForeign(variant.Key))
                .GroupBy(variant => variant.Key, StringComparer.Ordinal)
                .Where(group => group.Select(variant => variant.Value).Distinct(StringComparer.Ordinal).Count() == 1)
                .Select(group => group.First())
                .OrderBy(variant => variant.Key, StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<string> Spellings(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return [value.ToLowerInvariant(), value.ToUpperInvariant(), Capitalized(value)];
        }

        var tail = value[prefix.Length..];
        return [prefix + tail.ToLowerInvariant(), prefix + tail.ToUpperInvariant(), tail, tail.ToLowerInvariant(), tail.ToUpperInvariant()];
    }

    private static string Capitalized(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private IEnumerable<string> LegacyValues(string key) =>
        table.ValueReplacements.Where(entry => entry.Key == key).Select(entry => entry.From)
            .Concat(table.TagReplacements.SelectMany(entry => entry.PresentTags).Where(tag => tag.Key == key).Select(tag => tag.Value));
}
