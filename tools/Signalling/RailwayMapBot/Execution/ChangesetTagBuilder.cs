// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;

namespace RailwayMapBot.Execution;

// Builds the tags of a changeset from the configured ones and a comment
// template: who made the edit (the tool), that it is automated, where it is
// documented, and what it changes, in words a mapper can read.
public sealed class ChangesetTagBuilder(ChangesetSettings settings)
{
    private const int MaxValueLength = 255;

    public IReadOnlyDictionary<string, string> Build(string commentTemplate, string area, int batch)
    {
        var tags = settings.Tags.ToDictionary(tag => tag.Key, tag => Expand(tag.Value, area, batch), StringComparer.Ordinal);
        tags["comment"] = Expand(commentTemplate, area, batch);

        if (tags.FirstOrDefault(tag => tag.Value.Length > MaxValueLength) is { Key: not null } tooLong)
        {
            throw new InvalidOperationException($"The changeset tag '{tooLong.Key}' is longer than {MaxValueLength} characters: shorten its template.");
        }

        return tags;
    }

    private static string Expand(string template, string area, int batch) =>
        template
            .Replace("{area}", area, StringComparison.Ordinal)
            .Replace("{batch}", batch.ToString(), StringComparison.Ordinal)
            .Replace("{tool}", Banner.ProductVersion(), StringComparison.Ordinal);
}
