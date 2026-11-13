// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SignalBot.Tests;

// Plays the operator: the lines typed are given in advance, and what the
// program printed is kept. The console is shared by the whole process, so the
// tests using it are in the "Console" collection and run one after the other.
public sealed class ConsoleScript : IDisposable
{
    public const string Collection = "Console";

    private readonly TextReader _input = Console.In;
    private readonly TextWriter _output = Console.Out;
    private readonly StringWriter _printed = new();

    public ConsoleScript(params string[] lines)
    {
        Console.SetIn(new StringReader(string.Concat(lines.Select(line => line + Environment.NewLine))));
        Console.SetOut(_printed);
    }

    public string Printed => _printed.ToString();

    public void Dispose()
    {
        Console.SetIn(_input);
        Console.SetOut(_output);
    }
}
