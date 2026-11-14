// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// What a command of the interactive session is about to do, shown before the
// operator is asked to proceed: the command and the server on a first line,
// then the signals and the action, one short line each:
//   [apply] on server 'prod'
//     Signals: last edited by the 43 opted-in mappers
//     Action:  upload at most 200 fixes as 'signalbot'
public static class CommandRecap
{
    public static string Describe(CommandLineOptions options, BotSettings settings, BotState state, SignalSelection selection)
    {
        var account = state.Server.Account;
        var action = options.Command switch
        {
            BotCommand.Plan => "dry run, nothing is written",
            BotCommand.Apply => $"upload at most {options.Limit} fixes as '{account}'",
            BotCommand.Seed => $"create {options.Limit} test signals as '{account}', copied from signals of {settings.Country.Name} to fix",
            BotCommand.Revert => $"restore, as '{account}', the tags replaced by run {RunId(state.LastApplyJournal)}, on the signals nobody edited since",
            BotCommand.Logout => $"revoke the access token of '{account}', then forget the account and its client ID on this computer",
            _ => null,
        };

        List<string> lines = [$"[{options.Command.ToString().ToLowerInvariant()}] on server '{state.ServerName}'"];
        if (options.Command is BotCommand.Plan or BotCommand.Apply)
        {
            lines.Add($"  Signals: {selection.Describe(settings)}");
        }

        if (action is not null)
        {
            lines.Add($"  Action:  {action}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    // The name of the folder of a run is its id.
    private static string RunId(string? journalPath) =>
        journalPath is null ? "?" : Path.GetFileName(Path.GetDirectoryName(journalPath)) ?? "?";
}
