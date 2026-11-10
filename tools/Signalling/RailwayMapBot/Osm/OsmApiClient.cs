// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using System.Xml.Linq;
using RailwayMapBot.Core;

namespace RailwayMapBot.Osm;

// The few calls of the OSM API 0.6 the bot needs. A failed read is sent again
// after a pause; a write is never sent twice, since the server may have applied
// it: the caller reads the nodes again and decides.
public sealed class OsmApiClient(HttpClient client, LimitSettings limits)
{
    private const string ApiPath = "api/0.6/";

    // Current version of the nodes, deleted ones included (not visible). Ids
    // the server never knew make the whole request fail.
    public async Task<IReadOnlyList<OsmNode>> GetNodesAsync(IEnumerable<long> ids, CancellationToken cancellationToken)
    {
        List<OsmNode> nodes = [];
        foreach (var chunk in ids.Distinct().Chunk(limits.NodesPerRequest))
        {
            var document = await ReadAsync($"{ApiPath}nodes?nodes={string.Join(',', chunk)}", cancellationToken);
            nodes.AddRange(OsmXml.ReadNodes(document));
        }

        return nodes;
    }

    // Display name of the account the token belongs to.
    public async Task<string> GetAccountNameAsync(CancellationToken cancellationToken) =>
        OsmXml.ReadUserName(await ReadAsync($"{ApiPath}user/details", cancellationToken));

    public async Task<long> CreateChangesetAsync(IReadOnlyDictionary<string, string> tags, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Put, $"{ApiPath}changeset/create", OsmXml.WriteChangeset(tags), cancellationToken);
        return long.Parse(response.Trim());
    }

    // Uploads the change as a whole: the server applies all of it or nothing.
    public async Task<IReadOnlyDictionary<long, NodeResult>> UploadAsync(long changesetId, XDocument change, CancellationToken cancellationToken)
    {
        var response = await SendAsync(HttpMethod.Post, $"{ApiPath}changeset/{changesetId}/upload", change, cancellationToken);
        return OsmXml.ReadDiffResult(XDocument.Parse(response));
    }

    public Task CloseChangesetAsync(long changesetId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, $"{ApiPath}changeset/{changesetId}/close", null, cancellationToken);

    private async Task<XDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return XDocument.Parse(await SendAsync(HttpMethod.Get, path, null, cancellationToken));
            }
            catch (Exception ex) when (attempt < limits.ReadRetries && IsTransient(ex) && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"  Read failed ({ex.Message}), trying again in {limits.RetryDelaySeconds} s...");
                await Task.Delay(TimeSpan.FromSeconds(limits.RetryDelaySeconds), cancellationToken);
            }
        }
    }

    private async Task<string> SendAsync(HttpMethod method, string path, XDocument? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var retryAfter = response.Headers.RetryAfter;
            throw new OsmApiException(response.StatusCode, text, retryAfter?.Delta ?? retryAfter?.Date - DateTimeOffset.UtcNow);
        }

        return text;
    }

    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException || exception is OsmApiException { IsTransient: true };
}
