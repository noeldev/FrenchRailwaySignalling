// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SignalBot.Core;

namespace SignalBot.Auth;

// OAuth 2 authorization code flow with PKCE, for an application registered
// with the out-of-band redirect address: the operator opens the page, logs in
// with the bot account and pastes the code shown. The application is public:
// no password or client secret goes through the tool.
public sealed class OAuthLogin(HttpClient client, ServerSettings server)
{
    public const string RedirectUri = "urn:ietf:wg:oauth:2.0:oob";
    private const string Scopes = "read_prefs write_api";

    public async Task<string> AuthorizeAsync(string clientId, CancellationToken cancellationToken)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var site = server.WebUrl.TrimEnd('/');

        Console.WriteLine();
        Browser.Open(
            "the authorization page",
            $"{site}/oauth2/authorize?response_type=code"
                + $"&client_id={Uri.EscapeDataString(clientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}"
                + $"&scope={Uri.EscapeDataString(Scopes)}"
                + $"&code_challenge={challenge}&code_challenge_method=S256");
        Console.WriteLine("Click \"Authorize\", then copy the code shown.");
        var code = ConsolePrompt.Required("Authorization code");

        using var content = new FormUrlEncodedContent(
        [
            KeyValuePair.Create("grant_type", "authorization_code"),
            KeyValuePair.Create("code", code),
            KeyValuePair.Create("redirect_uri", RedirectUri),
            KeyValuePair.Create("client_id", clientId),
            KeyValuePair.Create("code_verifier", verifier),
        ]);

        using var response = await client.PostAsync($"{site}/oauth2/token", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The server refused the code ({(int)response.StatusCode}): {body}. Run login again; if it fails again, register a new application.");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("The server returned no access token.");
    }
}
