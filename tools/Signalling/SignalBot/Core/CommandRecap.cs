// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// One sentence telling exactly what a command of the interactive session is
// about to do, shown before the operator is asked to proceed: the command,
// the server, the signals, and what is written under which account.
public static class CommandRecap
{
    public static string Describe(CommandLineOptions options, BotSettings settings, BotState state, SignalSelection selection)
    {
        var head = $"[{options.Command.ToString().ToLowerInvariant()}] on server '{state.ServerName}'";
        var account = state.Server.Account;

        return options.Command switch
        {
            BotCommand.Plan => $"{head} for {selection.Describe(settings)}: dry run, nothing is written.",
            BotCommand.Apply => $"{head} for {selection.Describe(settings)}: upload at most {options.Limit} fixes as '{account}'.",
            BotCommand.Seed => $"{head}: create {options.Limit} test signals as '{account}', copied from signals of {settings.Country.Name} to fix.",
            BotCommand.Revert => $"{head}: restore, as '{account}', the tags replaced by run {RunId(state.LastApplyJournal)}, on the signals nobody edited since.",
            _ => $"{head}.",
        };
    }

    // The name of the folder of a run is its id.
    private static string RunId(string? journalPath) =>
        journalPath is null ? "?" : Path.GetFileName(Path.GetDirectoryName(journalPath)) ?? "?";
}
