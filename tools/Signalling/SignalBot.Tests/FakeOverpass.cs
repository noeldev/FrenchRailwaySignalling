// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Net.Http.Headers;

namespace SignalBot.Tests;

// An Overpass interpreter that answers what the test queued, one answer per
// request, and remembers what it was asked.
public sealed class FakeOverpass : HttpMessageHandler
{
    public const string Complete = """{ "osm3s": { "timestamp_osm_base": "2026-11-13T21:58:12Z" }, "elements": [ { "type": "node", "id": 1, "lat": 49.1, "lon": -0.3, "version": 2 } ] }""";

    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _answers = new();

    // Address, query and Authorization header of every request received.
    public List<(string Endpoint, string Query, AuthenticationHeaderValue? Authorization)> Requests { get; } = [];

    public HttpClient CreateClient() => new(this) { Timeout = Timeout.InfiniteTimeSpan };

    public FakeOverpass Answer(HttpStatusCode status, string body, int? retryAfterSeconds = null)
    {
        _answers.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return Task.FromResult(response);
        });
        return this;
    }

    // A server that never answers: the request ends when it is cancelled.
    public FakeOverpass Hang()
    {
        _answers.Enqueue(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        });
        return this;
    }

    public FakeOverpass Fail(Exception exception)
    {
        _answers.Enqueue(_ => Task.FromException<HttpResponseMessage>(exception));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = await request.Content!.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!.ToString(), Uri.UnescapeDataString(form["data=".Length..].Replace('+', ' ')), request.Headers.Authorization));
        return await _answers.Dequeue()(cancellationToken);
    }
}
