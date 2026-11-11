// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Core;

namespace RailwayMapBot.Auth;

// Guides the operator through the registration of the OAuth 2 application on
// the bot account (once per server) and returns its client ID. The form of
// the website is filled by hand: the tool only shows the values to type.
public static class ApplicationRegistration
{
    public static string Ask(string serverName, ServerSettings server, string applicationName)
    {
        var site = server.WebUrl.TrimEnd('/');
        Console.WriteLine($"The bot needs an OAuth 2 application registered on its account on '{serverName}' (once).");
        Console.WriteLine($"1. In the browser, log in to {site} with the bot account (create it first with");
        Console.WriteLine("   \"Sign Up\" if needed; the sandbox and the real server have separate accounts).");
        Console.WriteLine("2. On the page about to open, fill in the form:");
        Console.WriteLine($"     Name:                     {applicationName}");
        Console.WriteLine($"     Redirect URIs:            {OAuthLogin.RedirectUri}");
        Console.WriteLine("     Confidential application: unchecked");
        Console.WriteLine("     Permissions:              \"Read user preferences\" and \"Modify the map\" only");
        Console.WriteLine("   then click \"Register\". The client secret shown is not needed.");
        Browser.Open($"{site}/oauth2/applications/new", "the registration page");

        return ConsolePrompt.Required("3. Client ID shown by the website");
    }
}
