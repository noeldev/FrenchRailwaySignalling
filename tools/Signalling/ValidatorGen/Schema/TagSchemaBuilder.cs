// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Core.Model;
using Signalling.Wiki;
using ValidatorGen.Core;
using ValidatorGen.Wiki;

namespace ValidatorGen.Schema;

// Builds the documented scheme from the wiki pages:
// - every key declared under the configured prefix is documented;
// - a key is open when a scheme page declares it with a wildcard and no domain
//   page lists its values, closed otherwise;
// - "<key>:ref" is implied for every signal type key: the wiki documents the
//   idreseau ref once, generically, under the general tags.
public static class TagSchemaBuilder
{
    private const string RefSuffix = ":ref";

    public static TagSchema Build(IReadOnlyList<WikiPage> pages, string keyPrefix, string countryPrefix)
    {
        var values = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var wildcards = new HashSet<string>(StringComparer.Ordinal);
        var domainKeys = new HashSet<string>(StringComparer.Ordinal);
        var source = new WikiSource();

        foreach (var page in pages)
        {
            using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(page.Wikitext));
            foreach (var rule in source.ReadRules(content).Where(rule => rule.Key.StartsWith(keyPrefix, StringComparison.Ordinal)))
            {
                var keyValues = GetOrAdd(values, rule.Key);
                foreach (var value in rule.Matcher.EnumerateValues())
                {
                    keyValues.UnionWith(TagSchema.SplitTokens(value));
                }

                if (rule.Matcher is KeyPresent or RegexValue)
                {
                    wildcards.Add(rule.Key);
                }

                if (page.Role == WikiPageRole.Domain)
                {
                    domainKeys.Add(rule.Key);
                }
            }
        }

        var specs = values.ToDictionary(
            entry => entry.Key,
            entry => new KeySpec(
                entry.Key,
                entry.Value,
                entry.Value.Count == 0 || (wildcards.Contains(entry.Key) && !domainKeys.Contains(entry.Key))),
            StringComparer.Ordinal);

        foreach (var spec in specs.Values.Where(spec => spec.HasPrefixedValue).ToList())
        {
            specs.TryAdd(spec.Key + RefSuffix, new KeySpec(spec.Key + RefSuffix, new SortedSet<string>(StringComparer.Ordinal), true));
        }

        return new TagSchema(specs, countryPrefix);
    }

    private static SortedSet<string> GetOrAdd(Dictionary<string, SortedSet<string>> values, string key)
    {
        if (!values.TryGetValue(key, out var set))
        {
            set = new SortedSet<string>(StringComparer.Ordinal);
            values[key] = set;
        }

        return set;
    }
}
