// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using Signalling.Core;
using Signalling.Core.Model;
using YamlDotNet.RepresentationModel;

namespace Signalling.OrmVector;

// Reads the OpenRailwayMap-vector signals_railway_signals.yaml as a consumer
// source: the rules it exposes are the value constraints the map renders on.
//
// Only features of the configured country are read. For each feature, two kinds
// of constraint are collected:
//   - the "tags" clauses that select the feature (value / values / any / all, or
//     bare key presence). Here "value" is a matched tag value.
//   - the "icon" match components, whose cases discriminate the match tag on its
//     value (any / exact / regex / all). In a case, "value" is the icon path,
//     not a match, so it is ignored here.
//
// A "default" icon, or a case with no value constraint, means the match tag is
// rendered for any value: a key-level rule (KeyPresent) is added so the
// comparator treats every value of that key as covered.
//
// Set membership is used for "values" and "all": each listed value is treated as
// covered, which is what the tag/value coverage check needs even though "all"
// semantically requires every value to be present at once.
public sealed class OrmVectorSource(string country = "FR") : ISignalTagSource
{
    public string FormatName => "ORM-vector YAML";

    public string Country { get; } = country;

    public IReadOnlyList<SignalTagRule> ReadRules(Stream content)
    {
        var stream = new YamlStream();
        using (var reader = new StreamReader(content, leaveOpen: true))
        {
            stream.Load(reader);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            return [];
        }

        var rules = new List<SignalTagRule>();
        if (root.Child("features") is YamlSequenceNode features)
        {
            foreach (var feature in features.Children.OfType<YamlMappingNode>())
            {
                if (!string.Equals(feature.ScalarValue("country"), Country, StringComparison.Ordinal))
                {
                    continue;
                }

                var origin = feature.ScalarValue("description") ?? "(feature)";
                ReadFeatureTags(feature, origin, rules);
                ReadIconMatches(feature, origin, rules);
            }
        }

        return rules;
    }

    // The "tags" clauses that select a feature. Each clause constrains one key.
    private static void ReadFeatureTags(YamlMappingNode feature, string origin, List<SignalTagRule> rules)
    {
        if (feature.Child("tags") is not YamlSequenceNode tags)
        {
            return;
        }

        foreach (var clause in tags.Children.OfType<YamlMappingNode>())
        {
            var key = clause.ScalarValue("tag");
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            rules.Add(new SignalTagRule(key, FeatureTagMatcher(clause), origin));
        }
    }

    // The "icon" match components. Each component matches one key; its cases
    // discriminate that key's value.
    private static void ReadIconMatches(YamlMappingNode feature, string origin, List<SignalTagRule> rules)
    {
        if (feature.Child("icon") is not YamlSequenceNode icon)
        {
            return;
        }

        foreach (var component in icon.Children.OfType<YamlMappingNode>())
        {
            var matchKey = component.ScalarValue("match");
            if (string.IsNullOrEmpty(matchKey))
            {
                continue;
            }

            var rendersAnyValue = component.Child("default") is not null;

            if (component.Child("cases") is YamlSequenceNode cases)
            {
                foreach (var caseNode in cases.Children.OfType<YamlMappingNode>())
                {
                    var matcher = CaseMatcher(caseNode);
                    if (matcher is null)
                    {
                        // A case with no value constraint renders unconditionally.
                        rendersAnyValue = true;
                        continue;
                    }

                    rules.Add(new SignalTagRule(matchKey, matcher, origin));
                }
            }

            if (rendersAnyValue)
            {
                rules.Add(new SignalTagRule(matchKey, new KeyPresent(), origin));
            }
        }
    }

    // Matcher for a feature "tags" clause, where "value" is a matched tag value.
    private static Matcher FeatureTagMatcher(YamlMappingNode clause)
    {
        if (clause.Child("any") is YamlSequenceNode any)
        {
            return new AnyOf(YamlNodeExtensions.Scalars(any));
        }

        // "all" requires every value at once; treated as membership for coverage.
        if (clause.Child("all") is YamlSequenceNode all)
        {
            return new AnyOf(YamlNodeExtensions.Scalars(all));
        }

        if (clause.Child("values") is YamlSequenceNode values)
        {
            return new AnyOf(YamlNodeExtensions.Scalars(values));
        }

        var value = clause.ScalarValue("value");
        return value is not null ? new ExactValue(value) : new KeyPresent();
    }

    // Matcher for an icon "case", where "value" is the icon path (ignored here)
    // and the match is expressed by any / exact / regex / all. Returns null when
    // the case carries no value constraint.
    private static Matcher? CaseMatcher(YamlMappingNode caseNode)
    {
        if (caseNode.Child("any") is YamlSequenceNode any)
        {
            return new AnyOf(YamlNodeExtensions.Scalars(any));
        }

        // "all" requires every value at once; treated as membership for coverage.
        if (caseNode.Child("all") is YamlSequenceNode all)
        {
            return new AnyOf(YamlNodeExtensions.Scalars(all));
        }

        var regex = caseNode.ScalarValue("regex");
        if (regex is not null)
        {
            return new RegexValue(regex);
        }

        var exact = caseNode.ScalarValue("exact");
        return exact is not null ? new ExactValue(exact) : null;
    }
}
