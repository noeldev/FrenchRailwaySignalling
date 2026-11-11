// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;

namespace SignalBot.Auth;

// Guides the operator through the registration of the OAuth 2 application on
// the bot account (once per account) and returns its client ID. The form of
// the website is filled by hand: the tool only shows the values to type.
public static class ApplicationRegistration
{
    public static string Ask(string serverName, ServerSettings server, string applicationName)
    {
        Console.WriteLine();
        Console.WriteLine($"The bot needs an OAuth 2 application registered on its account on '{serverName}' (once).");
        Browser.Open("the registration page", $"{server.WebUrl.TrimEnd('/')}/oauth2/applications/new");
        Console.WriteLine("Fill in the form:");
        Console.WriteLine($"  Name:                     {applicationName}");
        Console.WriteLine($"  Redirect URIs:            {OAuthLogin.RedirectUri}");
        Console.WriteLine("  Confidential application: unchecked");
        Console.WriteLine("  Permissions:              check \"Read user preferences\" and \"Modify the map\" only");
        Console.WriteLine("Click \"Register\". The client secret shown is not needed.");
        return ConsolePrompt.Required("Client ID");
    }
}
