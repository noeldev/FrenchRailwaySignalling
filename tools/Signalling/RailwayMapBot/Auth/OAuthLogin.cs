// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RailwayMapBot.Core;

namespace RailwayMapBot.Auth;

// OAuth 2 authorization code flow with PKCE, for an application registered
// with the out-of-band redirect address: the operator opens the page, logs in
// with the bot account and pastes the code shown. No password or secret goes
// through the tool.
public sealed class OAuthLogin(HttpClient client, ServerSettings server)
{
    private const string RedirectUri = "urn:ietf:wg:oauth:2.0:oob";
    private const string Scopes = "read_prefs write_api";

    public async Task<string> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var site = server.WebUrl.TrimEnd('/');

        Console.WriteLine("Open this address in a browser where the bot account is logged in, then authorize:");
        Console.WriteLine();
        Console.WriteLine($"{site}/oauth2/authorize?response_type=code"
            + $"&client_id={Uri.EscapeDataString(server.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
            + $"&scope={Uri.EscapeDataString(Scopes)}"
            + $"&code_challenge={challenge}&code_challenge_method=S256");
        Console.WriteLine();
        Console.Write("Authorization code: ");
        var code = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException("No authorization code given.");
        }

        using var content = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("grant_type", "authorization_code"),
            KeyValuePair.Create("code", code),
            KeyValuePair.Create("redirect_uri", RedirectUri),
            KeyValuePair.Create("client_id", server.ClientId),
            KeyValuePair.Create("code_verifier", verifier),
        ]);

        using var response = await client.PostAsync($"{site}/oauth2/token", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The server refused the code ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("The server returned no access token.");
    }
}
