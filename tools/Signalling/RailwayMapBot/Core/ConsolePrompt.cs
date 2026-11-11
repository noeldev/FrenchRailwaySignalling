// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace RailwayMapBot.Core;

// Questions asked to the operator in the console. An empty answer takes the
// default shown between brackets.
public static class ConsolePrompt
{
    public static string Ask(string question, string defaultValue = "")
    {
        Console.Write(defaultValue.Length > 0 ? $"{question} [{defaultValue}]: " : $"{question}: ");
        var answer = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(answer) ? defaultValue : answer;
    }

    // Asks again until something is typed.
    public static string Required(string question)
    {
        while (true)
        {
            var answer = Ask(question);
            if (answer.Length > 0)
            {
                return answer;
            }
        }
    }

    public static bool YesNo(string question, bool defaultValue)
    {
        while (true)
        {
            var answer = Ask($"{question} (y/n)", defaultValue ? "y" : "n").ToLowerInvariant();
            if (answer is "y" or "yes" or "o" or "oui")
            {
                return true;
            }

            if (answer is "n" or "no" or "non")
            {
                return false;
            }
        }
    }

    public static int Number(string question, int defaultValue)
    {
        while (true)
        {
            if (int.TryParse(Ask(question, defaultValue.ToString()), out var number) && number > 0)
            {
                return number;
            }

            Console.WriteLine("  A positive number, please.");
        }
    }

    // One of the choices, by number or by name.
    public static string Choose(string question, IReadOnlyList<(string Name, string Description)> choices, string defaultName)
    {
        var width = choices.Max(choice => choice.Name.Length);
        for (var i = 0; i < choices.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {choices[i].Name.PadRight(width)}  {choices[i].Description}");
        }

        while (true)
        {
            var answer = Ask(question, defaultName);
            if (int.TryParse(answer, out var index) && index >= 1 && index <= choices.Count)
            {
                return choices[index - 1].Name;
            }

            var match = choices.FirstOrDefault(choice => choice.Name.Equals(answer, StringComparison.OrdinalIgnoreCase));
            if (match.Name is not null)
            {
                return match.Name;
            }
        }
    }

    // The operator types the name of the server, so a writing command is never
    // confirmed by a reflex.
    public static bool ConfirmOnServer(string action, string server)
    {
        Console.WriteLine();
        Console.WriteLine($"About to {action}.");
        Console.Write($"Type the server name ({server}) to confirm: ");
        if (Console.ReadLine()?.Trim() == server)
        {
            return true;
        }

        Console.WriteLine("Not confirmed: nothing was written.");
        return false;
    }

    public static void WaitForEnter(string message)
    {
        Console.Write($"{message} ");
        Console.ReadLine();
    }
}
