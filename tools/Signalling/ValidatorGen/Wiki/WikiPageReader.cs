// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;
using Signalling.Core.Sources;
using ValidatorGen.Core;

namespace ValidatorGen.Wiki;

// Reads the configured wiki pages. Online, they are downloaded in a single
// MediaWiki API query that also returns the timestamp of their last revision.
// With --local, every page is read from its local copy (the reviewed backup in
// wiki/backup/), and the file time stands for the revision time.
public sealed class WikiPageReader(HttpClient client, string repositoryRoot)
{
    public async Task<IReadOnlyList<WikiPage>> ReadAsync(
        WikiSettings settings,
        bool useLocalPages,
        CancellationToken cancellationToken)
    {
        if (settings.Pages.Count == 0)
        {
            throw new InvalidOperationException("No wiki page configured (Wiki:Pages).");
        }

        if (!useLocalPages)
        {
            return await DownloadAsync(settings.ApiUrl, settings.Pages, cancellationToken);
        }

        // Local mode never falls back to the live wiki: only reviewed copies
        // are used, so a page without a local copy is a configuration error.
        var missing = settings.Pages.Where(page => string.IsNullOrWhiteSpace(page.Local)).Select(page => page.Title).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"No local copy configured for: {string.Join(", ", missing)}");
        }

        var pages = new List<WikiPage>();
        foreach (var page in settings.Pages)
        {
            pages.Add(await ReadLocalAsync(page, cancellationToken));
        }

        return pages;
    }

    private async Task<WikiPage> ReadLocalAsync(WikiPageSettings page, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repositoryRoot, page.Local!);
        using var source = await new LocalFileResolver().OpenAsync(path, cancellationToken);
        using var reader = new StreamReader(source.Content);
        var wikitext = await reader.ReadToEndAsync(cancellationToken);

        return new WikiPage(page.Title, page.Role, wikitext, source.LastModified ?? DateTimeOffset.UnixEpoch, source.DisplayName);
    }

    private async Task<IReadOnlyList<WikiPage>> DownloadAsync(
        string apiUrl,
        IReadOnlyList<WikiPageSettings> pages,
        CancellationToken cancellationToken)
    {
        var titles = string.Join('|', pages.Select(page => page.Title));
        var url = $"{apiUrl}?action=query&prop=revisions&rvprop=timestamp%7Ccontent&rvslots=main"
            + $"&format=json&formatversion=2&titles={Uri.EscapeDataString(titles)}";

        using var source = await new HttpResolver(client).OpenAsync(url, cancellationToken);
        using var document = await JsonDocument.ParseAsync(source.Content, cancellationToken: cancellationToken);
        var query = document.RootElement.GetProperty("query");

        var revisions = new Dictionary<string, (string Wikitext, DateTimeOffset Timestamp)>(StringComparer.Ordinal);
        foreach (var page in query.GetProperty("pages").EnumerateArray())
        {
            var title = page.GetProperty("title").GetString() ?? string.Empty;
            if (page.TryGetProperty("missing", out _))
            {
                throw new InvalidOperationException($"Wiki page not found: {title}");
            }

            var revision = page.GetProperty("revisions")[0];
            var content = revision.GetProperty("slots").GetProperty("main").GetProperty("content").GetString() ?? string.Empty;
            revisions[title] = (content, revision.GetProperty("timestamp").GetDateTimeOffset());
        }

        return [.. pages.Select(page =>
        {
            var revision = revisions[NormalizeTitle(page.Title)];
            return new WikiPage(page.Title, page.Role, revision.Wikitext, revision.Timestamp, $"wiki: {page.Title}");
        })];
    }

    // MediaWiki returns titles with spaces instead of underscores.
    private static string NormalizeTitle(string title) => title.Replace('_', ' ');
}
