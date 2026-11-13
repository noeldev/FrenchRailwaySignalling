// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// One entry of the "Step" menu of the interactive session. Done marks a step
// already taken, so the default is the next one.
public sealed record SessionStep(string Name, string Description, bool Done);

// The steps that can be taken now on a server, in their order:
// - real map: plan, login (until logged in), apply (once logged in), revert
//   (after an apply), signals (choose other signals);
// - sandbox: login (until logged in), seed (once logged in), plan and apply
//   (once test nodes exist), revert (after an apply);
// then server (choose another server) and exit.
public static class SessionSteps
{
    public const string Signals = "signals";
    public const string Server = "server";
    public const string Exit = "exit";

    public static IReadOnlyList<SessionStep> For(BotState state)
    {
        var sandbox = state.Server.AllowSeeding;
        var applyReady = state.IsLoggedIn && (!sandbox || state.HasSeededNodes);
        List<SessionStep> steps = [];

        if (sandbox)
        {
            AddIf(steps, !state.IsLoggedIn, BotCommand.Login, "log in with the bot account of the sandbox", false);
            AddIf(steps, state.IsLoggedIn, BotCommand.Seed, "create test nodes, copied from real signals to fix", state.HasSeededNodes);
            AddIf(steps, state.HasSeededNodes, BotCommand.Plan, "dry run on the test nodes, nothing written", state.HasRun(BotCommand.Plan));
        }
        else
        {
            AddIf(steps, true, BotCommand.Plan, "dry run on the real data, nothing written", state.HasRun(BotCommand.Plan));
            AddIf(steps, !state.IsLoggedIn, BotCommand.Login, "log in with the bot account", false);
        }

        AddIf(steps, applyReady, BotCommand.Apply, sandbox ? "fix the test nodes" : "upload the fixes to the real map", state.LastApplyJournal is not null);
        AddIf(steps, state.LastApplyJournal is not null, BotCommand.Revert, "undo the last apply", true);
        if (!sandbox)
        {
            steps.Add(new SessionStep(Signals, "choose other signals", true));
        }

        steps.Add(new SessionStep(Server, "choose another server", true));
        steps.Add(new SessionStep(Exit, "quit", true));
        return steps;
    }

    private static void AddIf(List<SessionStep> steps, bool available, BotCommand command, string description, bool done)
    {
        if (available)
        {
            steps.Add(new SessionStep(command.ToString().ToLowerInvariant(), description, done));
        }
    }
}
