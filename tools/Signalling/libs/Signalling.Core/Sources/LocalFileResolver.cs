// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

namespace Signalling.Core.Sources;

// Resolves a spec as a local file path. This is the default resolver, matching
// the preference for local inputs which are usually ahead of the online copies.
public sealed class LocalFileResolver : ISourceResolver
{
    // Anything that is not an http/https URL is treated as a local path.
    public bool CanResolve(string spec) => !HttpResolver.IsHttpUrl(spec);

    public Task<ResolvedSource> OpenAsync(string spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(spec);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Source file not found: {fullPath}", fullPath);
        }

        var content = File.OpenRead(fullPath);
        var lastModified = new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
        return Task.FromResult(new ResolvedSource(content, fullPath, lastModified));
    }
}
