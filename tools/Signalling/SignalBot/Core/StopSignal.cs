// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Core;

// Ctrl+C during a writing command asks it to stop after the current batch,
// its changeset closed; a second Ctrl+C ends the program at once, for
// example while a question waits for an answer. Ctrl+C anywhere else ends the
// program as usual.
public sealed class StopSignal : IDisposable
{
    private CancellationTokenSource? _current;
    private bool _graceful;

    public StopSignal() => Console.CancelKeyPress += OnCancelKeyPress;

    // The token of the next command. Graceful: Ctrl+C stops it after the
    // current batch instead of ending the program.
    public CancellationToken Start(bool graceful)
    {
        End();
        _current = new CancellationTokenSource();
        _graceful = graceful;
        return _current.Token;
    }

    // The command is over: Ctrl+C ends the program again.
    public void End()
    {
        _current?.Dispose();
        _current = null;
        _graceful = false;
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        End();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (_current is null || !_graceful || _current.IsCancellationRequested)
        {
            return;
        }

        e.Cancel = true;
        Console.WriteLine("Stopping after the current batch (Ctrl+C again to quit now)...");
        _current.Cancel();
    }
}
