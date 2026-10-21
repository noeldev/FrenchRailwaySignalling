// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Signalling.Schema;

// The documented tagging scheme: every checked key with its specification.
// Values of every key are compared token by token (";" separated), since the
// states keys and a few others hold lists. A token with a namespace prefix other
// than the country prefix (CH-FDV:, ETCS:...) belongs to another scheme and is
// out of scope: it is always accepted.
public sealed partial class TagSchema(IReadOnlyDictionary<string, KeySpec> keys, string countryPrefix)
{
    public const char ValueSeparator = ';';

    private readonly string _countryNamespace = countryPrefix + ":";

    // Every documented key, sorted by key so the output is stable.
    public IReadOnlyList<KeySpec> Keys { get; } = [.. keys.Values.OrderBy(spec => spec.Key, StringComparer.Ordinal)];

    public bool IsDocumentedKey(string key) => keys.ContainsKey(key);

    public bool TryGetKey(string key, [MaybeNullWhen(false)] out KeySpec spec) => keys.TryGetValue(key, out spec);

    // True when the value is accepted for the key: the key is documented and
    // either open, or every token of the value is a listed or foreign value.
    public bool IsDocumentedValue(string key, string value) =>
        keys.TryGetValue(key, out var spec)
        && (spec.IsOpen || SplitTokens(value).All(token => spec.Values.Contains(token) || IsForeign(token)));

    // The keys under the prefix that the wiki does not document, and the tokens
    // of closed keys that it does not list ("key=token"), as the validator
    // reports them.
    public IEnumerable<string> FindUndocumented(IReadOnlyDictionary<string, string> tags, string keyPrefix)
    {
        foreach (var (key, value) in tags.Where(tag => tag.Key.StartsWith(keyPrefix, StringComparison.Ordinal)))
        {
            if (!keys.TryGetValue(key, out var spec))
            {
                yield return key;
                continue;
            }

            if (spec.IsOpen)
            {
                continue;
            }

            foreach (var token in SplitTokens(value).Where(token => !spec.Values.Contains(token) && !IsForeign(token)))
            {
                yield return $"{key}={token}";
            }
        }
    }

    // True for a value of another scheme, for example CH-FDV:l or ETCS:marker.
    public bool IsForeign(string value) =>
        HasNamespacePrefix(value) && !value.StartsWith(_countryNamespace, StringComparison.OrdinalIgnoreCase);

    // True for a value of the checked scheme, for example FR:C.
    public bool IsDomestic(string value) => value.StartsWith(_countryNamespace, StringComparison.Ordinal);

    public static IEnumerable<string> SplitTokens(string value) =>
        value.Split(ValueSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public static bool HasNamespacePrefix(string value) => NamespacePrefixRegex().IsMatch(value);

    // "FR:", "CH-FDV:", "ETCS:", "PL-PKP:" ... The same shape is spelled in
    // MapCSS by ValidatorRuleBuilder.ForeignTokenPattern.
    [GeneratedRegex(@"^[A-Z][A-Za-z]*(?:-[A-Za-z]+)*:")]
    private static partial Regex NamespacePrefixRegex();
}
