// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

namespace Signalling.Core.Sources;

// Resolves a spec as an http/https URL. The HttpClient is injected so the host
// can share its timeout, decompression and User-Agent settings across every
// network check.
public sealed class HttpResolver(HttpClient client) : ISourceResolver
{
    public static bool IsHttpUrl(string spec) =>
        Uri.TryCreate(spec, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public bool CanResolve(string spec) => IsHttpUrl(spec);

    public async Task<ResolvedSource> OpenAsync(string spec, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(spec, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var lastModified = response.Content.Headers.LastModified;

        // Buffer into memory so the returned stream outlives the response, which
        // is disposed as this method returns.
        var buffer = new MemoryStream();
        await using (var network = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await network.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        return new ResolvedSource(buffer, spec, lastModified);
    }
}
