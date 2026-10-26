// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.RegularExpressions;
using ValidatorGen.Core;
using static ValidatorGen.MapCss.MapCssSyntax;

namespace ValidatorGen.Generation;

// The classes shared by the generated rules:
// - the scope class, set on the nodes with a value of the country (FR:..., or
//   FR alone) in one of the checked keys. Every other rule is restricted to it
//   (or to its tag condition), so the validator never reports the signals of
//   other countries;
// - one class per key that a legacy rule deals with, set when the rule matches.
//   The rules for undocumented keys and values skip a key with this class, so a
//   legacy tag is reported once, by the rule that fixes it.
// The self-test nodes get a tag of the scope when they have none, so they are
// checked like map data.
public sealed partial class RuleScope(ValidatorSettings settings, IReadOnlyList<KeyValuePair<string, string>> samples)
{
    private const string HandledClassPrefix = "legacy_";

    private readonly Regex _scopeValue = new($"^{CaseInsensitive(settings.CountryPrefix)}(:|$)");

    public string ClassName { get; } = settings.CountryPrefix.ToLowerInvariant() + "_signal";

    // A tag of the scope, for the self-tests.
    public KeyValuePair<string, string> Sample { get; } = samples[0];

    // The tag condition of the scope, for a selector that must match without
    // the classes (see ValidatorRuleBuilder).
    public string Condition =>
        $"[/^{EscapeRegex(settings.KeyPrefix)}/=~/^{CaseInsensitive(settings.CountryPrefix)}(:|$)/]";

    public string Selector => "node" + Condition;

    // A selector restricted to the scope. The class goes last, so the
    // {N.key} placeholders keep pointing at the tag conditions.
    public string Restrict(string selector) => $"{selector}.{ClassName}";

    // The class of a key dealt with by a legacy rule: the key without the
    // checked prefix, with "__" for the colons (a class name has no colon).
    public string HandledClass(string key)
    {
        var name = key.StartsWith(settings.KeyPrefix, StringComparison.Ordinal) ? key[settings.KeyPrefix.Length..] : key;
        return HandledClassPrefix + NonClassCharacterRegex().Replace(name.Replace(":", "__", StringComparison.Ordinal), "_");
    }

    public string SetHandled(string key) => $"set .{HandledClass(key)}";

    // The tags of a self-test node, with a scope sample in front when no tag
    // puts the node in the scope (one of another key than the node tags).
    public IReadOnlyList<KeyValuePair<string, string>> InScope(IEnumerable<KeyValuePair<string, string>> tags)
    {
        var list = tags.ToList();
        if (list.Any(IsScoping))
        {
            return list;
        }

        var sample = samples.First(sample => list.All(tag => tag.Key != sample.Key));
        return [sample, .. list];
    }

    private bool IsScoping(KeyValuePair<string, string> tag) =>
        tag.Key.StartsWith(settings.KeyPrefix, StringComparison.Ordinal) && _scopeValue.IsMatch(tag.Value);

    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex NonClassCharacterRegex();
}
