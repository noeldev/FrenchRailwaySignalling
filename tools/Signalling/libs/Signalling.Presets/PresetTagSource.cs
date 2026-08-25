// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Xml.Linq;
using Signalling.Core;
using Signalling.Core.Model;

namespace Signalling.Presets;

// Reads a JOSM preset as a producer source: every emittable tag becomes a rule.
// A fixed value maps to ExactValue; an open-ended numeric combo, or a key with
// no fixed value (a free text field), maps to KeyPresent, so the comparator
// falls back to a key-level check where no closed value domain exists.
public sealed class PresetTagSource(bool useFrench, IEnumerable<string> excludedKeys, string country = "FR")
    : ISignalTagSource
{
    private readonly bool _useFrench = useFrench;
    private readonly IReadOnlyList<string> _excludedKeys = [.. excludedKeys];

    public string FormatName => "JOSM preset";

    public string Country { get; } = country;

    public IReadOnlyList<SignalTagRule> ReadRules(Stream content)
    {
        var document = XDocument.Load(content);
        var parser = new PresetParser(document, _useFrench, _excludedKeys);

        var rules = new List<SignalTagRule>();
        foreach (var tag in parser.Parse(document))
        {
            rules.Add(new SignalTagRule(tag.Key, ToMatcher(tag), tag.Description ?? tag.Key));
        }

        return rules;
    }

    private static Matcher ToMatcher(PresetTag tag)
    {
        if (tag.IsOpenDomain || tag.Value is null)
        {
            return new KeyPresent();
        }

        return new ExactValue(tag.Value);
    }
}
