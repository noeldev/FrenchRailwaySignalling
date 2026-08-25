// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

namespace Signalling.Core.Sources;

// Turns a source spec (a local path or an http/https URL) into an opened stream
// with its provenance. Keeping resolution behind this interface lets format
// readers stay transport agnostic and lets new transports (cache, git, ...) be
// added without touching the readers.
public interface ISourceResolver
{
    // True when this resolver recognizes the spec, by scheme or shape.
    bool CanResolve(string spec);

    // Opens the content and returns it with its provenance. The caller owns the
    // returned instance and disposes it.
    Task<ResolvedSource> OpenAsync(string spec, CancellationToken cancellationToken);
}

// An opened source: its readable content, a display name for the report and,
// when known, the last modification time so the report states exactly which
// version of each input was compared.
public sealed class ResolvedSource(Stream content, string displayName, DateTimeOffset? lastModified) : IDisposable
{
    public Stream Content { get; } = content;

    public string DisplayName { get; } = displayName;

    public DateTimeOffset? LastModified { get; } = lastModified;

    public void Dispose() => Content.Dispose();
}
