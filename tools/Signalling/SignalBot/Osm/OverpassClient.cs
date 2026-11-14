// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SignalBot.Core;

namespace SignalBot.Osm;

// Runs the configured query for one area, with an optional mapper filter
// (the nodes whose last version is by one of the given mappers, see
// UserFilter and UidFilter). Overpass only lists the candidates: every node
// is read again from the OSM server before it is edited. The answer can be
// saved as received, to be read again with --input.
//
// Overpass is a shared, often loaded server, so a failure is expected:
// - the query carries its own time limit ({timeout}), and the request waits
//   a little longer than that, so the client never gives up on a query the
//   server is still running;
// - a transient failure (no answer in time, 429 or 5xx, connection cut off,
//   incomplete or truncated answer) is tried again, on the next endpoint
//   when several are configured, after a wait that doubles each time (or
//   the wait the server asks for);
// - any other failure (a query the server refuses) ends at once;
// - an answer is used only when it is complete, and saved only then.
// When every try failed, the command fails before anything is written.
// The client must be one of its own, without the credentials of the OSM
// server (see OsmHttpClient.CreateOverpass).
public sealed partial class OverpassClient(HttpClient client, OverpassSettings settings)
{
    public const string AreaPlaceholder = "{area}";
    public const string MapperPlaceholder = "{mapper}";
    public const string TimeoutPlaceholder = "{timeout}";

    // Time left to the server to send its answer once the query is over.
    private static readonly TimeSpan TransferMargin = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(10);

    private const int MaxErrorLength = 200;

    public async Task<OverpassAnswer> QueryAsync(string areaSelector, string mapperFilter, string? savePath, CancellationToken cancellationToken)
    {
        var query = string.Join('\n', settings.Query)
            .Replace(TimeoutPlaceholder, settings.TimeoutSeconds.ToString(), StringComparison.Ordinal)
            .Replace(AreaPlaceholder, areaSelector, StringComparison.Ordinal)
            .Replace(MapperPlaceholder, mapperFilter, StringComparison.Ordinal);

        for (var attempt = 0; ; attempt++)
        {
            var endpoint = settings.Endpoints[attempt % settings.Endpoints.Count];
            try
            {
                var body = await SendAsync(endpoint, query, cancellationToken);
                var answer = OverpassReader.Read(new MemoryStream(body, writable: false));
                if (savePath is not null)
                {
                    await File.WriteAllBytesAsync(savePath, body, cancellationToken);
                }

                return answer;
            }
            catch (OverpassException ex) when (ex.IsTransient && attempt < settings.Retries)
            {
                var delay = ex.RetryAfter ?? TimeSpan.FromSeconds(settings.RetryDelaySeconds * Math.Pow(2, attempt));
                delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay < MaxRetryDelay ? delay : MaxRetryDelay;
                Console.WriteLine($"  Overpass ({new Uri(endpoint).Host}): {ex.Message}.");
                Console.WriteLine($"  Trying again in {Math.Ceiling(delay.TotalSeconds)} s (try {attempt + 2} of {settings.Retries + 1})...");
                await Task.Delay(delay, cancellationToken);
            }
            catch (OverpassException ex)
            {
                throw new OverpassException($"Overpass ({new Uri(endpoint).Host}): {ex.Message}. Nothing was read.", ex.IsTransient);
            }
        }
    }

    // The body of a successful answer. Every failure is an OverpassException;
    // only the request of the operator to stop goes through as a cancellation.
    private async Task<byte[]> SendAsync(string endpoint, string query, CancellationToken cancellationToken)
    {
        var limit = TimeSpan.FromSeconds(settings.TimeoutSeconds) + TransferMargin;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limit);

        try
        {
            using var content = new FormUrlEncodedContent([KeyValuePair.Create("data", query)]);
            using var response = await client.PostAsync(endpoint, content, deadline.Token);
            var body = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            if (response.IsSuccessStatusCode)
            {
                return body;
            }

            var status = (int)response.StatusCode;
            var retryAfter = response.Headers.RetryAfter;
            throw new OverpassException(
                $"{status} {response.ReasonPhrase}{DescribeError(body)}",
                isTransient: response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || status >= 500,
                retryAfter?.Delta ?? retryAfter?.Date - DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OverpassException($"no answer within {limit.TotalSeconds} s", isTransient: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new OverpassException($"the connection failed ({ex.Message})", isTransient: true);
        }
    }

    // The reason of an error, in one short line. Overpass explains a refused
    // query in an HTML page (<p><strong>Error</strong>: line 2: parse error
    // ...</p>), its front server explains a refusal in a text with "#"
    // headings: the error paragraphs of the first, the first sentence of the
    // second.
    private static string DescribeError(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        var errors = ErrorParagraphRegex().Matches(text);
        var reason = errors.Count > 0
            ? string.Join(" ", errors.Select(error => error.Groups[1].Value))
            : text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith('<')) ?? string.Empty;

        reason = WhitespaceRegex().Replace(MarkupRegex().Replace(reason, " "), " ").Trim().TrimStart(':').Trim();
        var sentence = reason.IndexOf(". ", StringComparison.Ordinal);
        reason = (sentence > 0 ? reason[..sentence] : reason).TrimEnd('.');

        return reason.Length == 0 ? string.Empty
            : reason.Length <= MaxErrorLength ? $" ({reason})"
            : $" ({reason[..MaxErrorLength]}...)";
    }

    // Overpass filter on the display name of the user of the last version;
    // quotes and backslashes are escaped in the name.
    public static string UserFilter(string name) =>
        $"(user:\"{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\")";

    // Overpass filter on the uid of the user of the last version.
    public static string UidFilter(IEnumerable<long> uids) => $"(uid:{string.Join(',', uids)})";

    [GeneratedRegex("<p>\\s*<strong[^>]*>\\s*Error\\s*</strong>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ErrorParagraphRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex MarkupRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
