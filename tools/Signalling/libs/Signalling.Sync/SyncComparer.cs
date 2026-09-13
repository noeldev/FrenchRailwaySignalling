// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Core.Model;

namespace Signalling.Sync;

// Compares the tag rules of a producer (the JOSM preset) against those of a
// consumer (the ORM map, or the wiki specification). Two asymmetric passes are
// run: producer against consumer, then consumer against producer. The scope
// controls whether producer keys the consumer lacks are reported (AllKeys, for
// an authority consumer) or ignored (ConsumerKeys, for a subset consumer).
//
// Open value domains meet at the key level: a producer open domain (numeric
// combo, free text) or a consumer catch-all (default, regular expression) cannot
// be enumerated, so it is compared by key presence rather than value by value.
//
// A value delimited with ";" (a multiselect combo emitting several selections
// at once) is not required to appear as that exact joined string on the other
// side: it is also accepted when every individual token is covered there, even
// though the token never appears alone as its own value. This is checked in
// both directions, so a documented single value is not reported missing just
// because the producer only ever emits it combined with others.
//
// Findings are neutral: severity and wording are decided by the caller.
public sealed class SyncComparer
{
    private const char MultiselectDelimiter = ';';

    public IReadOnlyList<SyncFinding> Compare(
        IReadOnlyList<SignalTagRule> producerRules,
        IReadOnlyList<SignalTagRule> consumerRules,
        SyncScope scope)
    {
        var producerByKey = GroupByKey(producerRules);
        var consumerByKey = GroupByKey(consumerRules);

        var findings = new List<SyncFinding>();
        CompareProducerAgainstConsumer(producerByKey, consumerByKey, scope, findings);
        CompareConsumerAgainstProducer(consumerByKey, producerByKey, findings);
        return Normalize(findings);
    }

    private static void CompareProducerAgainstConsumer(
        IReadOnlyDictionary<string, List<SignalTagRule>> producerByKey,
        IReadOnlyDictionary<string, List<SignalTagRule>> consumerByKey,
        SyncScope scope,
        List<SyncFinding> findings)
    {
        foreach (var (key, producerRules) in producerByKey)
        {
            if (!consumerByKey.TryGetValue(key, out var consumerRules))
            {
                if (scope == SyncScope.AllKeys)
                {
                    findings.Add(new SyncFinding(SyncFindingKind.ProducerKeyUnmatched, key, null, producerRules[0].Origin));
                }

                continue;
            }

            var consumerMatchers = consumerRules.Select(rule => rule.Matcher).ToList();
            var consumerRendersAnyValue = consumerMatchers.Any(matcher => matcher is KeyPresent);
            var consumerHasPattern = consumerMatchers.Any(matcher => matcher is RegexValue);

            foreach (var producerRule in producerRules)
            {
                var values = producerRule.Matcher.EnumerateValues();
                if (values.Count == 0)
                {
                    if (!consumerRendersAnyValue && !consumerHasPattern)
                    {
                        findings.Add(new SyncFinding(SyncFindingKind.ProducerOpenDomainPartial, key, null, producerRule.Origin));
                    }

                    continue;
                }

                foreach (var value in values)
                {
                    if (IsCoveredByConsumer(value, consumerMatchers))
                    {
                        continue;
                    }

                    findings.Add(new SyncFinding(SyncFindingKind.ProducerValueUnmatched, key, value, producerRule.Origin));
                }
            }
        }
    }

    private static void CompareConsumerAgainstProducer(
        IReadOnlyDictionary<string, List<SignalTagRule>> consumerByKey,
        IReadOnlyDictionary<string, List<SignalTagRule>> producerByKey,
        List<SyncFinding> findings)
    {
        foreach (var (key, consumerRules) in consumerByKey)
        {
            if (!producerByKey.TryGetValue(key, out var producerRules))
            {
                findings.Add(new SyncFinding(SyncFindingKind.ConsumerKeyUnmatched, key, null, consumerRules[0].Origin));
                continue;
            }

            var producerMatchers = producerRules.Select(rule => rule.Matcher).ToList();
            if (producerMatchers.Any(matcher => matcher is KeyPresent))
            {
                // The producer emits an open domain on this key: any value is
                // emittable, so no consumer value can be stale.
                continue;
            }

            var producerTokens = CollectTokens(producerRules);

            foreach (var consumerRule in consumerRules)
            {
                foreach (var value in consumerRule.Matcher.EnumerateValues())
                {
                    if (producerMatchers.Any(matcher => matcher.Matches(value)) || producerTokens.Contains(value))
                    {
                        continue;
                    }

                    findings.Add(new SyncFinding(SyncFindingKind.ConsumerValueUnmatched, key, value, consumerRule.Origin));
                }
            }
        }
    }

    // Whether a producer value is covered by the consumer, either as an exact
    // match or, for a ";"-joined multiselect value, token by token.
    private static bool IsCoveredByConsumer(string value, List<Matcher> consumerMatchers)
    {
        if (consumerMatchers.Any(matcher => matcher.Matches(value)))
        {
            return true;
        }

        var tokens = SplitTokens(value);
        return tokens.Length > 1 && tokens.All(token => consumerMatchers.Any(matcher => matcher.Matches(token)));
    }

    // The individual tokens across every value a set of producer rules can
    // emit, so a consumer value can be matched against one piece of a
    // multiselect combo even when that piece never appears alone.
    private static HashSet<string> CollectTokens(List<SignalTagRule> producerRules)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in producerRules)
        {
            foreach (var value in rule.Matcher.EnumerateValues())
            {
                foreach (var token in SplitTokens(value))
                {
                    tokens.Add(token);
                }
            }
        }

        return tokens;
    }

    private static string[] SplitTokens(string value) =>
        value.Split(MultiselectDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyDictionary<string, List<SignalTagRule>> GroupByKey(IReadOnlyList<SignalTagRule> rules)
    {
        var index = new Dictionary<string, List<SignalTagRule>>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (!index.TryGetValue(rule.Key, out var list))
            {
                list = [];
                index[rule.Key] = list;
            }

            list.Add(rule);
        }

        return index;
    }

    // De-duplicates findings that repeat across items or features, then sorts for
    // a stable report.
    private static IReadOnlyList<SyncFinding> Normalize(IEnumerable<SyncFinding> findings)
    {
        return [.. findings
            .GroupBy(finding => (finding.Kind, finding.Key, finding.Value ?? string.Empty))
            .Select(group => group.First())
            .OrderBy(finding => finding.Key, StringComparer.Ordinal)
            .ThenBy(finding => finding.Value ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(finding => finding.Kind)];
    }
}
