// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

namespace Signalling.Core.Sources;

// Dispatches a spec to the first resolver that recognizes it. New transports are
// added by registering another resolver, without changing callers. The local
// resolver is expected last, since it accepts any non-URL spec as a fallback.
public sealed class CompositeSourceResolver(IReadOnlyList<ISourceResolver> resolvers) : ISourceResolver
{
    public bool CanResolve(string spec) => resolvers.Any(resolver => resolver.CanResolve(spec));

    public Task<ResolvedSource> OpenAsync(string spec, CancellationToken cancellationToken)
    {
        foreach (var resolver in resolvers)
        {
            if (resolver.CanResolve(spec))
            {
                return resolver.OpenAsync(spec, cancellationToken);
            }
        }

        throw new NotSupportedException($"No resolver can handle the source spec: {spec}");
    }
}
