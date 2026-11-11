// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Xml.Linq;
using SignalBot.Osm;

namespace SignalBot.Tests;

// An in-memory OSM API 0.6, as strict as the real one where it matters: an
// upload is applied as a whole or refused as a whole, and a node sent with
// another version than the current one is a conflict.
public sealed class FakeOsmServer : HttpMessageHandler
{
    public const string Account = "TestBot";

    private long _nextChangeset = 100;
    private long _nextNode = 5000;

    public Dictionary<long, OsmNode> Nodes { get; } = [];

    // Earlier versions of the nodes, for their history.
    public List<OsmNode> History { get; } = [];

    public List<FakeChangeset> Changesets { get; } = [];

    // Number of requests that changed something on the server.
    public int Writes { get; private set; }

    public int NodeReads { get; private set; }

    // Runs once, just before the next upload is checked: another mapper
    // editing in between.
    public Action? BeforeNextUpload { get; set; }

    // Answers the next upload with 429 and this Retry-After (seconds).
    public int? RateLimitNextUpload { get; set; }

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("https://osm.test/") };

    public void Add(long id, int version, IReadOnlyDictionary<string, string> tags, string user = "mapper", DateTimeOffset? timestamp = null) =>
        Nodes[id] = new OsmNode(id, version, true, 48.1234567, 2.7654321, timestamp ?? DateTimeOffset.UnixEpoch, user, tags);

    // An edit by someone else: new tags, next version.
    public void EditAs(string user, long id, IReadOnlyDictionary<string, string> tags) =>
        Replace(Nodes[id] with { Version = Nodes[id].Version + 1, Tags = tags, User = user, Timestamp = DateTimeOffset.UnixEpoch });

    private void Replace(OsmNode node)
    {
        History.Add(Nodes[node.Id]);
        Nodes[node.Id] = node;
    }

    public void Delete(long id) => Nodes[id] = Nodes[id] with { Version = Nodes[id].Version + 1, Visible = false };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath.TrimStart('/');
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        return (request.Method.Method, path.Split('/')) switch
        {
            ("GET", ["api", "0.6", "nodes"]) => ReadNodes(request.RequestUri.Query),
            ("GET", ["api", "0.6", "node", var id, "history"]) => Ok(WriteNodes(History.Where(node => node.Id == long.Parse(id)).Append(Nodes[long.Parse(id)]))),
            ("GET", ["api", "0.6", "user", "details"]) => Ok($"<osm><user id=\"1\" display_name=\"{Account}\"/></osm>"),
            ("PUT", ["api", "0.6", "changeset", "create"]) => CreateChangeset(body),
            ("POST", ["api", "0.6", "changeset", var id, "upload"]) => Upload(long.Parse(id), body),
            ("PUT", ["api", "0.6", "changeset", var id, "close"]) => Close(long.Parse(id)),
            _ => Answer(HttpStatusCode.NotFound, $"No route for {request.Method} {path}"),
        };
    }

    private HttpResponseMessage ReadNodes(string query)
    {
        NodeReads++;
        var ids = query[(query.IndexOf('=') + 1)..].Split(',').Select(long.Parse).ToList();
        if (ids.Any(id => !Nodes.ContainsKey(id)))
        {
            return Answer(HttpStatusCode.NotFound, string.Empty);
        }

        return Ok(WriteNodes(ids.Select(id => Nodes[id])));
    }

    private static string WriteNodes(IEnumerable<OsmNode> nodes) =>
        new XElement("osm", nodes.Select(node => new XElement("node",
            new XAttribute("id", node.Id),
            new XAttribute("visible", node.Visible ? "true" : "false"),
            new XAttribute("version", node.Version),
            new XAttribute("timestamp", node.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")),
            new XAttribute("user", node.User),
            node.Visible ? new XAttribute("lat", node.Lat.ToString("0.0000000")) : null,
            node.Visible ? new XAttribute("lon", node.Lon.ToString("0.0000000")) : null,
            node.Tags.Select(tag => new XElement("tag", new XAttribute("k", tag.Key), new XAttribute("v", tag.Value)))))).ToString();

    private HttpResponseMessage CreateChangeset(string body)
    {
        Writes++;
        var tags = XDocument.Parse(body).Descendants("tag").ToDictionary(tag => (string)tag.Attribute("k")!, tag => (string)tag.Attribute("v")!);
        var changeset = new FakeChangeset(_nextChangeset++, tags);
        Changesets.Add(changeset);
        return Ok(changeset.Id.ToString());
    }

    private HttpResponseMessage Upload(long changesetId, string body)
    {
        if (RateLimitNextUpload is { } seconds)
        {
            RateLimitNextUpload = null;
            var limited = Answer(HttpStatusCode.TooManyRequests, "Upload has been blocked due to rate limiting.");
            limited.Headers.RetryAfter = new(TimeSpan.FromSeconds(seconds));
            return limited;
        }

        BeforeNextUpload?.Invoke();
        BeforeNextUpload = null;

        var changeset = Changesets.Single(entry => entry.Id == changesetId);
        if (!changeset.IsOpen)
        {
            return Answer(HttpStatusCode.Conflict, $"The changeset {changesetId} was closed.");
        }

        var change = XDocument.Parse(body).Root!;
        var modified = change.Elements("modify").Elements("node").ToList();

        // All or nothing: check every node before changing any.
        foreach (var node in modified)
        {
            var current = Nodes[(long)node.Attribute("id")!];
            if (!current.Visible)
            {
                return Answer(HttpStatusCode.Gone, $"The node with the id {current.Id} has already been deleted");
            }

            if ((int)node.Attribute("version")! != current.Version)
            {
                return Answer(HttpStatusCode.Conflict, $"Version mismatch: Provided {(int)node.Attribute("version")!}, server had: {current.Version} of Node {current.Id}");
            }
        }

        Writes++;
        var result = new XElement("diffResult");
        foreach (var node in modified)
        {
            var id = (long)node.Attribute("id")!;
            Replace(Nodes[id] with { Version = Nodes[id].Version + 1, Tags = ReadTags(node), User = Account, Timestamp = DateTimeOffset.UtcNow });
            result.Add(new XElement("node", new XAttribute("old_id", id), new XAttribute("new_id", id), new XAttribute("new_version", Nodes[id].Version)));
        }

        foreach (var node in change.Elements("create").Elements("node"))
        {
            var id = _nextNode++;
            Nodes[id] = new OsmNode(id, 1, true, (double)node.Attribute("lat")!, (double)node.Attribute("lon")!, DateTimeOffset.UtcNow, Account, ReadTags(node));
            result.Add(new XElement("node", new XAttribute("old_id", (long)node.Attribute("id")!), new XAttribute("new_id", id), new XAttribute("new_version", 1)));
        }

        changeset.NodeCount += result.Elements().Count();
        return Ok(result.ToString());
    }

    private HttpResponseMessage Close(long changesetId)
    {
        Writes++;
        Changesets.Single(entry => entry.Id == changesetId).IsOpen = false;
        return Ok(string.Empty);
    }

    private static Dictionary<string, string> ReadTags(XElement node) =>
        node.Elements("tag").ToDictionary(tag => (string)tag.Attribute("k")!, tag => (string)tag.Attribute("v")!);

    private static HttpResponseMessage Ok(string content) => Answer(HttpStatusCode.OK, content);

    private static HttpResponseMessage Answer(HttpStatusCode status, string content) => new(status) { Content = new StringContent(content) };
}

public sealed class FakeChangeset(long id, IReadOnlyDictionary<string, string> tags)
{
    public long Id { get; } = id;

    public IReadOnlyDictionary<string, string> Tags { get; } = tags;

    public bool IsOpen { get; set; } = true;

    public int NodeCount { get; set; }
}
