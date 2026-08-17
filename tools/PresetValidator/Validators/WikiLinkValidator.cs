// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Collections.Concurrent;
using System.Xml.Linq;
using PresetValidator.Core;

namespace PresetValidator.Validators;

// Optional network check. Verifies that wiki links resolve (HTTP success) and,
// when an anchor is present, that the target page actually contains it. Anchor
// matching against MediaWiki output is best effort, so a missing anchor is
// reported as a warning rather than an error.
public sealed class WikiLinkValidator : IValidator
{
    private const string WikiBaseUrl = "https://wiki.openstreetmap.org/wiki/";
    private const int MaxConcurrency = 4;

    public string Name => "Wiki links";

    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(ValidationContext context, CancellationToken cancellationToken)
    {
        var targets = CollectTargets(context.Document);
        if (targets.Count == 0)
        {
            return [];
        }

        using var client = HttpClientFactory.Create(context.Options.TimeoutSeconds);
        using var throttle = new SemaphoreSlim(MaxConcurrency);
        var pageCache = new ConcurrentDictionary<string, Task<PageResult>>();

        var checks = targets.Select(target => CheckTargetAsync(target, client, pageCache, throttle, cancellationToken));
        var results = await Task.WhenAll(checks);

        return results.Where(issue => issue is not null).Select(issue => issue!).ToList();
    }

    private static List<WikiTarget> CollectTargets(XDocument document)
    {
        var targets = new List<WikiTarget>();

        foreach (var element in document.Descendants().Where(e => e.Name.LocalName == "link"))
        {
            var href = element.Attribute("href")?.Value;
            if (!string.IsNullOrWhiteSpace(href))
            {
                targets.Add(new WikiTarget(element, href));
            }

            // The wiki attribute holds a page title, optionally with an anchor
            // (for example "Tagging_in_France#Stops").
            var wiki = element.Attribute("wiki")?.Value;
            if (!string.IsNullOrWhiteSpace(wiki))
            {
                targets.Add(new WikiTarget(element, WikiBaseUrl + wiki.Replace(' ', '_')));
            }
        }

        return targets;
    }

    private static async Task<ValidationIssue?> CheckTargetAsync(
        WikiTarget target,
        HttpClient client,
        ConcurrentDictionary<string, Task<PageResult>> pageCache,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return target.Element.ToIssue(ValidationSeverity.Warning, $"Malformed link URL: {target.Url}");
        }

        var pageUrl = uri.GetLeftPart(UriPartial.Query);
        var fragment = Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));

        var page = await pageCache.GetOrAdd(pageUrl, url => FetchPageAsync(url, client, throttle, cancellationToken));

        if (!page.Success)
        {
            return target.Element.ToIssue(ValidationSeverity.Error, $"Broken link ({page.StatusText}): {pageUrl}");
        }

        if (fragment.Length > 0 && !AnchorExists(page.Content, fragment))
        {
            return target.Element.ToIssue(ValidationSeverity.Warning, $"Anchor '#{fragment}' not found on page: {pageUrl}");
        }

        return null;
    }

    private static async Task<PageResult> FetchPageAsync(
        string url,
        HttpClient client,
        SemaphoreSlim throttle,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken);
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return PageResult.Failed($"HTTP {(int)response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return PageResult.Ok(content);
        }
        catch (HttpRequestException ex)
        {
            return PageResult.Failed(ex.Message);
        }
        catch (TaskCanceledException)
        {
            return PageResult.Failed("timeout");
        }
        finally
        {
            throttle.Release();
        }
    }

    // MediaWiki renders section anchors as id attributes. Spaces map to
    // underscores, so both variants are accepted.
    private static bool AnchorExists(string html, string fragment)
    {
        var underscored = fragment.Replace(' ', '_');
        return html.Contains($"id=\"{fragment}\"", StringComparison.Ordinal)
            || html.Contains($"id=\"{underscored}\"", StringComparison.Ordinal);
    }

    private sealed record WikiTarget(XElement Element, string Url);

    private readonly record struct PageResult(bool Success, string Content, string StatusText)
    {
        public static PageResult Ok(string content) => new(true, content, "OK");

        public static PageResult Failed(string statusText) => new(false, string.Empty, statusText);
    }
}
