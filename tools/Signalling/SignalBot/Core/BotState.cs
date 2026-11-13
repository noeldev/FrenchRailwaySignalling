// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Security.Cryptography;
using System.Text;
using SignalBot.Auth;
using SignalBot.Reporting;

namespace SignalBot.Core;

// Where the files of a country and a server are, and how far the work went:
// logged in, test nodes seeded, apply runs to revert.
// The commands read it to find their files, the interactive session to offer
// only the steps that can be taken.
public sealed class BotState(BotSettings settings, string serverName, string repositoryRoot)
{
    public string ServerName => serverName;

    public ServerSettings Server => settings.Servers.TryGetValue(serverName, out var server)
        ? server
        : throw new ArgumentException($"Unknown server: {serverName}. Configured: {string.Join(", ", settings.Servers.Keys)}");

    public string RulesPath => Path.GetFullPath(Path.Combine(repositoryRoot, settings.Rules));

    // Hash of the text with LF line ends, so it does not depend on how git
    // checked the file out (CRLF on Windows, LF elsewhere).
    public string RulesHash => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(File.ReadAllText(RulesPath).ReplaceLineEndings("\n"))));

    // Output folder of the country.
    public string OutputRoot => Path.GetFullPath(Path.Combine(repositoryRoot, settings.Output, settings.Country.Code));

    // Ids of the test nodes of the last seed on the server.
    public string SeededNodesPath => Path.Combine(OutputRoot, $"seeded-nodes-{serverName}.txt");

    // The bot account of the server is known and has a token.
    public bool IsLoggedIn => Server.Account.Length > 0 && new TokenStore(TokenStore.DefaultPath).Find(serverName) is not null;

    public bool HasSeededNodes => File.Exists(SeededNodesPath);

    // The journal of the last apply run on the server, unless a revert ran
    // after it. Run folders start with their start time, so their names sort
    // in time order.
    public string? LastApplyJournal
    {
        get
        {
            var apply = LastRunFolder(BotCommand.Apply);
            var journal = apply is null ? null : Path.Combine(apply, RunJournal.FileName);
            return journal is not null && File.Exists(journal) && string.CompareOrdinal(apply, LastRunFolder(BotCommand.Revert)) > 0 ? journal : null;
        }
    }

    public bool HasRun(BotCommand command) => LastRunFolder(command) is not null;

    private string? LastRunFolder(BotCommand command) =>
        Directory.Exists(OutputRoot)
            ? Directory.GetDirectories(OutputRoot, $"*-{command.ToString().ToLowerInvariant()}-{serverName}").Max(StringComparer.Ordinal)
            : null;
}
