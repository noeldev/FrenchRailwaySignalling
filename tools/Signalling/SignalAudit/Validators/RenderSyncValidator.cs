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

        IReadOnlyList<SignalTagRule> consumerRules;
        using (var yaml = await resolver.OpenAsync(source, cancellationToken))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Info, $"Map YAML: {SourceProvenance.Describe(yaml)}"));
            consumerRules = new OrmVectorSource().ReadRules(yaml.Content);
        }

        foreach (var finding in new SyncComparer().Compare(producerRules, consumerRules, SyncScope.ConsumerKeys))
        {
            issues.Add(SyncIssueFormatter.ToIssue(finding, "the map"));
        }

        return issues;
    }
}
