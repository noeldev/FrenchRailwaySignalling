// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Core.Model;
using Signalling.Core.Sources;
using Signalling.Presets;
using Signalling.Sync;
using Signalling.Wiki;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Cross-source check: compares the preset against the OSM wiki specification,
// which is the source of truth. The comparison spans all keys (AllKeys), so a
// preset key or value the wiki does not document is an error (the preset drifted
// from the spec), while a documented tag missing from the preset is a warning.
public sealed class WikiSyncValidator : IValidator
{
    public string Name => "Preset/wiki synchronization";

    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var source = context.Options.WikiSyncSource;
        if (string.IsNullOrWhiteSpace(source))
        {
            return [new ValidationIssue(ValidationSeverity.Warning, "No wiki source; content sync skipped.")];
        }

        using var client = HttpClientFactory.Create(context.Options.TimeoutSeconds);
        var resolver = new CompositeSourceResolver([new HttpResolver(client), new LocalFileResolver()]);

        var issues = new List<ValidationIssue>();

        IReadOnlyList<SignalTagRule> producerRules;
        using (var preset = await resolver.OpenAsync(context.Options.XmlPath, cancellationToken))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Info, $"Preset: {SourceProvenance.Describe(preset)}"));
            producerRules = new PresetTagSource(useFrench: false, excludedKeys: []).ReadRules(preset.Content);
        }

        producerRules = EtcsPendingCoverage.ExcludePending(producerRules);

        IReadOnlyList<SignalTagRule> authorityRules;
        using (var wiki = await resolver.OpenAsync(source, cancellationToken))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Info, $"Wiki: {SourceProvenance.Describe(wiki)}"));
            authorityRules = new WikiSource().ReadRules(wiki.Content);
        }

        authorityRules = WithImpliedRefCoverage(producerRules, authorityRules);

        foreach (var finding in new SyncComparer().Compare(producerRules, authorityRules, SyncScope.AllKeys))
        {
            issues.Add(SyncIssueFormatter.ToIssue(finding, "the OSM wiki"));
        }

        return issues;
    }

    // The wiki documents the idreseau reference tag once, generically, under
    // "General tags" ("railway:signal:<category>:ref"), in wikitext that is not
    // in the "* {{Tag|...}}" shape WikiSource parses. Every preset key ending in
    // ":ref" is implied to be covered by that clause regardless of category, so
    // a synthetic wildcard rule is added for each one the preset actually uses.
    private static IReadOnlyList<SignalTagRule> WithImpliedRefCoverage(
        IReadOnlyList<SignalTagRule> producerRules,
        IReadOnlyList<SignalTagRule> authorityRules)
    {
        var documentedKeys = new HashSet<string>(authorityRules.Select(rule => rule.Key), StringComparer.Ordinal);
        var expanded = authorityRules.ToList();

        foreach (var key in producerRules.Select(rule => rule.Key).Distinct(StringComparer.Ordinal))
        {
            if (key.EndsWith(":ref", StringComparison.Ordinal) && documentedKeys.Add(key))
            {
                expanded.Add(new SignalTagRule(key, new KeyPresent(), "General tags (idreseau, implied)"));
            }
        }

        return expanded;
    }
}
