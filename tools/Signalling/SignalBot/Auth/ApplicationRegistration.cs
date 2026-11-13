// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Core;

namespace SignalBot.Auth;

// Guides the operator to the client ID of the OAuth 2 application of the bot
// account: registered once per account, then read back from the list of the
// applications of the account. The forms of the website are filled by hand:
// the tool only shows the values to type.
public static class ApplicationRegistration
{
    public static string Ask(string serverName, ServerSettings server, string applicationName)
    {
        var applications = $"{server.WebUrl.TrimEnd('/')}/oauth2/applications";
        Console.WriteLine();
        Console.WriteLine($"The bot needs an OAuth 2 application registered on its account on '{serverName}' (once).");
        if (ConsolePrompt.YesNo($"Is the application already registered on '{serverName}'?", defaultValue: false))
        {
            Browser.Open("the list of applications", applications);
            Console.WriteLine($"Click the name of the application ('{applicationName}') to see its client ID.");
        }
        else
        {
            Browser.Open("the registration page", $"{applications}/new");
            Console.WriteLine("Fill in the form:");
            Console.WriteLine($"  Name:                     {applicationName}");
            Console.WriteLine($"  Redirect URIs:            {OAuthLogin.RedirectUri}");
            Console.WriteLine("  Confidential application: unchecked");
            Console.WriteLine("  Permissions:              check \"Read user preferences (read_prefs)\" and \"Modify the map (write_api)\" only");
            Console.WriteLine("Click \"Register\". The client secret shown is not needed.");
        }

        return ConsolePrompt.Required("Client ID");
    }
}
