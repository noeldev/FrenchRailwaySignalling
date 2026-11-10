// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Core;

// Checks the settings before anything else runs, so a broken configuration
// never reaches a server. Returns every problem found, not only the first.
public static class SettingsValidator
{
    // Limits of the OSM API: length of a tag value, size of a changeset.
    private const int MaxTagValueLength = 255;
    private const int MaxChangesetSize = 10000;

    private const string AreaPlaceholder = "{area}";

    public static IReadOnlyList<string> Validate(BotSettings settings)
    {
        List<string> errors = [];

        Require(errors, settings.KeyPrefix, "keyPrefix");
        Require(errors, settings.CountryPrefix, "countryPrefix");
        Require(errors, settings.Rules, "rules");
        Require(errors, settings.Output, "output");
        Require(errors, settings.Contact, "contact");
        Require(errors, settings.Overpass.Endpoint, "overpass:endpoint");
        Require(errors, settings.Changeset.FixComment, "changeset:fixComment");
        Require(errors, settings.Changeset.RevertComment, "changeset:revertComment");
        Require(errors, settings.Changeset.SeedComment, "changeset:seedComment");

        if (settings.Scope.Count == 0)
        {
            errors.Add("scope: at least one tag is required.");
        }

        if (!settings.Servers.ContainsKey(settings.DefaultServer))
        {
            errors.Add($"defaultServer: '{settings.DefaultServer}' is not one of the servers.");
        }

        foreach (var (name, server) in settings.Servers)
        {
            RequireUrl(errors, server.ApiUrl, $"servers:{name}:apiUrl");
            RequireUrl(errors, server.WebUrl, $"servers:{name}:webUrl");
            if (server.QuietPeriodHours < 0)
            {
                errors.Add($"servers:{name}:quietPeriodHours must not be negative.");
            }
        }

        if (!settings.Overpass.Query.Any(line => line.Contains(AreaPlaceholder, StringComparison.Ordinal)))
        {
            errors.Add($"overpass:query must contain {AreaPlaceholder}.");
        }

        if (settings.Areas.Count == 0 || settings.Areas.Any(area => string.IsNullOrWhiteSpace(area.Code) || string.IsNullOrWhiteSpace(area.Name)))
        {
            errors.Add("areas: at least one area, each with a code and a name.");
        }

        // The policy asks for these on every changeset of an automated edit.
        if (settings.Changeset.Tags.GetValueOrDefault("bot") != "yes" && settings.Changeset.Tags.GetValueOrDefault("mechanical") != "yes")
        {
            errors.Add("changeset:tags must hold bot=yes or mechanical=yes.");
        }

        if (!Uri.TryCreate(settings.Changeset.Tags.GetValueOrDefault("description"), UriKind.Absolute, out _))
        {
            errors.Add("changeset:tags:description must be the address of the page documenting the edit.");
        }

        foreach (var (key, value) in settings.Changeset.Tags.Where(tag => tag.Value.Length > MaxTagValueLength))
        {
            errors.Add($"changeset:tags:{key} is longer than {MaxTagValueLength} characters ({value.Length}).");
        }

        var limits = settings.Limits;
        RequireRange(errors, limits.MaxEditsPerRun, 1, int.MaxValue, "limits:maxEditsPerRun");
        RequireRange(errors, limits.MaxNodesPerChangeset, 1, MaxChangesetSize, "limits:maxNodesPerChangeset");
        RequireRange(errors, limits.NodesPerRequest, 1, 700, "limits:nodesPerRequest");
        RequireRange(errors, limits.DelayBetweenChangesetsSeconds, 0, 3600, "limits:delayBetweenChangesetsSeconds");
        RequireRange(errors, limits.ConflictRetries, 0, 10, "limits:conflictRetries");
        RequireRange(errors, limits.ReadRetries, 0, 10, "limits:readRetries");
        RequireRange(errors, limits.RetryDelaySeconds, 0, 3600, "limits:retryDelaySeconds");
        RequireRange(errors, limits.MaxRateLimitWaitSeconds, 0, 3600, "limits:maxRateLimitWaitSeconds");
        RequireRange(errors, limits.RequestTimeoutSeconds, 1, 3600, "limits:requestTimeoutSeconds");

        return errors;
    }

    private static void Require(List<string> errors, string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is missing.");
        }
    }

    private static void RequireUrl(List<string> errors, string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{name} must be an https address.");
        }
    }

    private static void RequireRange(List<string> errors, int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            errors.Add($"{name} must be between {min} and {max} (found {value}).");
        }
    }
}
