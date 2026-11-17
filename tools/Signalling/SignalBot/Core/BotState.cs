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

    // The journal of the newest apply run on the server that uploaded edits
    // and has not been reverted yet: reverting again and again undoes the
    // apply runs from the newest to the oldest. Only the runs of this server
    // count: the folder of a run ends with the name of its server.
    public string? LastApplyJournal
    {
        get
        {
            var applies = RunFolders(BotCommand.Apply);
            var reverted = RevertedRuns(applies);
            return applies
                .Where(folder => !reverted.Contains(Path.GetFileName(folder)))
                .Select(folder => Path.Combine(folder, RunJournal.FileName))
                .FirstOrDefault(HasUploads);
        }
    }

    public bool HasRun(BotCommand command) => RunFolders(command).Count > 0;

    // The run folders of a command on the server, newest first. Their names
    // start with their start time, so they sort in time order.
    private IReadOnlyList<string> RunFolders(BotCommand command) =>
        Directory.Exists(OutputRoot)
            ? [.. Directory.GetDirectories(OutputRoot, $"*-{command.ToString().ToLowerInvariant()}-{serverName}").OrderDescending(StringComparer.Ordinal)]
            : [];

    // The names of the apply runs undone by the revert runs of the server. A
    // revert journal names the run it undid; one written before it did so
    // undid the newest apply run started before it.
    private HashSet<string> RevertedRuns(IReadOnlyList<string> applies)
    {
        HashSet<string> reverted = new(StringComparer.Ordinal);
        foreach (var folder in RunFolders(BotCommand.Revert))
        {
            var journal = Path.Combine(folder, RunJournal.FileName);
            var undone = File.Exists(journal)
                ? ReadRunOrNull(journal)?.Reverts ?? applies.FirstOrDefault(apply => string.CompareOrdinal(apply, folder) < 0)
                : null;
            if (undone is not null)
            {
                reverted.Add(Path.GetFileName(undone));
            }
        }

        return reverted;
    }

    // An apply run stopped before its first upload has nothing to revert. A
    // journal that cannot be read is left to the revert command, which
    // reports why when it is given the journal.
    private static bool HasUploads(string journal)
    {
        try
        {
            return File.Exists(journal) && RunJournal.ReadUploads(journal).Edits.Count > 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static RunInfo? ReadRunOrNull(string journal)
    {
        try
        {
            return RunJournal.ReadRun(journal);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
