// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Core;

// Ctrl+C while a command runs asks it to stop after the current batch, its
// changeset closed; the session then goes on. Outside a command, Ctrl+C ends
// the program as usual.
public sealed class StopSignal : IDisposable
{
    private CancellationTokenSource? _current;

    public StopSignal() => Console.CancelKeyPress += OnCancelKeyPress;

    // The token of the next command.
    public CancellationToken Start()
    {
        _current?.Dispose();
        _current = new CancellationTokenSource();
        return _current.Token;
    }

    // The command is over: Ctrl+C ends the program again.
    public void End()
    {
        _current?.Dispose();
        _current = null;
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        End();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        e.Cancel = true;
        Console.WriteLine("Stopping after the current batch...");
        _current.Cancel();
    }
}
