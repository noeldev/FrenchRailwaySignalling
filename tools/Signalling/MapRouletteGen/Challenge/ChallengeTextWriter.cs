// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using MapRouletteGen.Core;

namespace MapRouletteGen.Challenge;

// Writes the texts to paste into the MapRoulette challenge form (name,
// description, instruction) followed by the statistics of the challenge:
// nodes checked, tasks, nodes fixed by each rule and conflicts left to the
// JOSM validator.
public static class ChallengeTextWriter
{
    public static void Write(string path, ChallengeSettings settings, ChallengeContent content, string inputName)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {settings.Name}");
        text.AppendLine();
        text.AppendLine("## Description");
        text.AppendLine();
        text.AppendLine(settings.Description.Trim());
        text.AppendLine();
        text.AppendLine("## Instruction");
        text.AppendLine();
        text.AppendLine(settings.Instruction.Trim());
        text.AppendLine();
        text.AppendLine("## Statistics");
        text.AppendLine();
        text.AppendLine($"- Input: {inputName}");
        text.AppendLine($"- Nodes checked: {content.NodeCount}");
        text.AppendLine($"- Tasks (nodes with legacy tagging): {content.Tasks.Count}");
        text.AppendLine();

        if (content.RuleCounts.Count > 0)
        {
            text.AppendLine("| Legacy tagging | Nodes |");
            text.AppendLine("|---|---:|");
            foreach (var (rule, count) in content.RuleCounts)
            {
                text.AppendLine($"| `{rule}` | {count} |");
            }

            text.AppendLine();
        }

        if (content.ConflictCounts.Count > 0)
        {
            text.AppendLine("Conflicts left to the JOSM validator (not fixed automatically):");
            text.AppendLine();
            foreach (var (conflict, count) in content.ConflictCounts.OrderByDescending(entry => entry.Value))
            {
                text.AppendLine($"- `{conflict}`: {count}");
            }

            text.AppendLine();
        }

        File.WriteAllText(path, text.ToString().ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
    }
}
