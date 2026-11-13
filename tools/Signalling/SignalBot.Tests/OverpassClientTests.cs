// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using SignalBot.Core;
using SignalBot.Osm;

namespace SignalBot.Tests;

// Overpass fails often (timeouts, load): what the client does then.
public sealed class OverpassClientTests
{
    private const string Main = "https://overpass.test/api/interpreter";
    private const string Mirror = "https://mirror.test/api/interpreter";

    private readonly FakeOverpass _server = new();

    private readonly OverpassSettings _settings = new()
    {
        Endpoints = [Main],
        TimeoutSeconds = 600,
        Retries = 2,
        RetryDelaySeconds = 0,
        Query = ["[out:json][timeout:{timeout}];", "{area}->.scope;", "node(area.scope)[railway=signal]{mapper};", "out meta;"],
    };

    private Task<OverpassAnswer> QueryAsync(string? savePath = null, CancellationToken cancellationToken = default) =>
        new OverpassClient(_server.CreateClient(), _settings).QueryAsync("area[name=Calvados]", "(uid:7)", savePath, cancellationToken);

    [Fact]
    public async Task Query_fills_the_time_limit_the_area_and_the_mapper()
    {
        _server.Answer(HttpStatusCode.OK, FakeOverpass.Complete);

        var answer = await QueryAsync();

        var request = Assert.Single(_server.Requests);
        Assert.Equal("[out:json][timeout:600];\narea[name=Calvados]->.scope;\nnode(area.scope)[railway=signal](uid:7);\nout meta;", request.Query);
        Assert.Equal(1, Assert.Single(answer.Nodes).Id);
        Assert.NotNull(answer.DataTime);
    }

    [Fact]
    public async Task Query_tries_again_after_a_gateway_timeout_and_an_incomplete_answer()
    {
        _server
            .Answer(HttpStatusCode.GatewayTimeout, "<html><body><p><strong>Error</strong>: runtime error: open64: 0 Success</p></body></html>")
            .Answer(HttpStatusCode.OK, """{ "elements": [], "remark": "runtime error: Query timed out" }""")
            .Answer(HttpStatusCode.OK, FakeOverpass.Complete);

        var answer = await QueryAsync();

        Assert.Equal(3, _server.Requests.Count);
        Assert.Single(answer.Nodes);
    }

    [Fact]
    public async Task Query_tries_again_after_a_cut_connection_and_a_truncated_answer()
    {
        _server
            .Fail(new HttpRequestException("The response ended prematurely."))
            .Answer(HttpStatusCode.OK, """{ "elements": [ { "type": "node", "id": 1, """)
            .Answer(HttpStatusCode.OK, FakeOverpass.Complete);

        Assert.Single((await QueryAsync()).Nodes);
    }

    [Fact]
    public async Task Query_waits_as_asked_when_the_rate_is_limited()
    {
        _settings.RetryDelaySeconds = 3600;
        _server
            .Answer(HttpStatusCode.TooManyRequests, "Too Many Requests", retryAfterSeconds: 0)
            .Answer(HttpStatusCode.OK, FakeOverpass.Complete);

        Assert.Single((await QueryAsync()).Nodes);
    }

    [Fact]
    public async Task Query_gives_up_on_a_server_that_does_not_answer_in_time()
    {
        _settings.TimeoutSeconds = -59;
        _settings.Retries = 1;
        _server.Hang().Hang();

        var error = await Assert.ThrowsAsync<OverpassException>(() => QueryAsync());

        Assert.Equal(2, _server.Requests.Count);
        Assert.Contains("no answer within 1 s", error.Message);
        Assert.Contains("Nothing was read", error.Message);
    }

    [Fact]
    public async Task Query_fails_once_every_try_failed()
    {
        _server
            .Answer(HttpStatusCode.ServiceUnavailable, string.Empty)
            .Answer(HttpStatusCode.ServiceUnavailable, string.Empty)
            .Answer(HttpStatusCode.ServiceUnavailable, string.Empty);

        var error = await Assert.ThrowsAsync<OverpassException>(() => QueryAsync());

        Assert.Equal(3, _server.Requests.Count);
        Assert.Contains("overpass.test", error.Message);
        Assert.Contains("503", error.Message);
    }

    [Fact]
    public async Task Query_does_not_try_again_a_query_the_server_refuses()
    {
        _server.Answer(HttpStatusCode.BadRequest, "<html><body><p><strong style=\"color:#FF0000\">Error</strong>: line 2: parse error: Unknown type \"aera\" </p></body></html>");

        var error = await Assert.ThrowsAsync<OverpassException>(() => QueryAsync());

        Assert.Single(_server.Requests);
        Assert.False(error.IsTransient);
        Assert.Contains("Error : line 2: parse error: Unknown type \"aera\"", error.Message);
    }

    [Fact]
    public async Task Query_goes_to_the_next_endpoint_after_a_failure()
    {
        _settings.Endpoints = [Main, Mirror];
        _server
            .Answer(HttpStatusCode.GatewayTimeout, string.Empty)
            .Answer(HttpStatusCode.OK, FakeOverpass.Complete);

        await QueryAsync();

        Assert.Equal(Main, _server.Requests[0].Endpoint);
        Assert.Equal(Mirror, _server.Requests[1].Endpoint);
    }

    [Fact]
    public async Task Query_stops_at_once_when_the_operator_asks()
    {
        _server.Hang();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QueryAsync(cancellationToken: stop.Token));

        Assert.Single(_server.Requests);
    }

    [Fact]
    public async Task Query_saves_only_a_complete_answer()
    {
        var folder = Directory.CreateTempSubdirectory("signalbot-").FullName;
        try
        {
            var path = Path.Combine(folder, "overpass-14.json");
            _settings.Retries = 0;
            _server.Answer(HttpStatusCode.OK, """{ "elements": [], "remark": "runtime error: Query ran out of memory" }""");
            await Assert.ThrowsAsync<OverpassException>(() => QueryAsync(path));
            Assert.False(File.Exists(path));

            _server.Answer(HttpStatusCode.OK, FakeOverpass.Complete);
            await QueryAsync(path);
            Assert.Equal(FakeOverpass.Complete, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // The access token of the bot account is for the OSM server only.
    [Fact]
    public async Task Overpass_client_never_carries_the_credentials_of_the_osm_server()
    {
        var settings = new BotSettings { Contact = "https://example.org/bot", Limits = { RequestTimeoutSeconds = 30 } };
        using var osm = OsmHttpClient.Create(settings, new ServerSettings { ApiUrl = "https://api.osm.test" });
        osm.DefaultRequestHeaders.Authorization = new("Bearer", "secret");
        using var overpass = OsmHttpClient.CreateOverpass(settings);

        Assert.Null(overpass.DefaultRequestHeaders.Authorization);
        Assert.Null(overpass.BaseAddress);
        Assert.Equal(Timeout.InfiniteTimeSpan, overpass.Timeout);
        Assert.Contains("https://example.org/bot", overpass.DefaultRequestHeaders.UserAgent.ToString());

        _server.Answer(HttpStatusCode.OK, FakeOverpass.Complete);
        await QueryAsync();
        Assert.Null(_server.Requests[0].Authorization);
    }
}
