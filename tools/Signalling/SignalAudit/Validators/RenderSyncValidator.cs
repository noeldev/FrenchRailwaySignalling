// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Core.Model;
using Signalling.Core.Sources;
using Signalling.OrmVector;
using Signalling.Presets;
using Signalling.Sync;
using SignalAudit.Core;

namespace SignalAudit.Validators;

// Cross-source check: compares the values the preset can emit against the values
// the ORM-vector map renders, scoped to the keys the map discriminates on. The
// map renders a legitimate subset, so producer keys it ignores are not reported.
public sealed class RenderSyncValidator : IValidator
{
    // The preset also carries a handful of CH-FDV signal types for cross-border
    // compatibility near the Swiss border, documented under the YAML's CH
    // country section rather than FR. The preset is not a Swiss signalling set
    // though, so only the CH-FDV values it already emits are pulled in from
    // that section (see FilterToProducerCoverage); the rest of Switzerland's
    // signalling, which the map documents in full, is left out of scope.
    private static readonly string[] MapCountries = ["FR", "CH"];

    public string Name => "Preset/map synchronization";

    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var source = context.Options.OrmVectorYamlSource;
        if (string.IsNullOrWhiteSpace(source))
        {
            return [new ValidationIssue(ValidationSeverity.Warning, "No ORM-vector YAML source; step skipped.")];
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

        IReadOnlyList<SignalTagRule> consumerRules;
        using (var yaml = await resolver.OpenAsync(source, cancellationToken))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Info, $"Map YAML: {SourceProvenance.Describe(yaml)}"));

            using var buffer = new MemoryStream();
            await yaml.Content.CopyToAsync(buffer, cancellationToken);

            var producerValues = CollectValues(producerRules);
            consumerRules = [.. MapCountries.SelectMany(country =>
            {
                buffer.Position = 0;
                var rules = new OrmVectorSource(country).ReadRules(buffer);
                return country == "FR" ? rules : FilterToProducerCoverage(rules, producerValues);
            })];
        }

        foreach (var finding in new SyncComparer().Compare(producerRules, consumerRules, SyncScope.ConsumerKeys))
        {
            issues.Add(SyncIssueFormatter.ToIssue(finding, "the map"));
        }

        return issues;
    }

    // A non-FR country section (CH) documents that country's own signalling in
    // full, most of which this preset has no reason to implement. Only the
    // values the preset already emits are kept, so the comparison neither
    // demands the rest of that section nor loses the cross-border values the
    // preset does carry.
    private static IReadOnlyList<SignalTagRule> FilterToProducerCoverage(
        IReadOnlyList<SignalTagRule> consumerRules,
        HashSet<string> producerValues)
    {
        return [.. consumerRules.Where(rule =>
            rule.Matcher.EnumerateValues() is { Count: > 0 } values && values.All(producerValues.Contains))];
    }

    // A preset value can be a ";"-joined multiselect combo (see SyncComparer),
    // so each token is collected on its own: a compound "CH-FDV:308;CH-FDV:310"
    // must still be recognized as covering "CH-FDV:308" alone.
    private static HashSet<string> CollectValues(IReadOnlyList<SignalTagRule> rules)
    {
        var tokens = rules
            .SelectMany(rule => rule.Matcher.EnumerateValues())
            .SelectMany(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return new HashSet<string>(tokens, StringComparer.Ordinal);
    }
}
