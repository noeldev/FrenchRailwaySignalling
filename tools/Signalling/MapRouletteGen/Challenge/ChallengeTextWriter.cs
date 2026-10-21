// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;
using MapRouletteGen.Core;

namespace MapRouletteGen.Challenge;

// Writes the texts to paste into the MapRoulette challenge form (name,
// description, instruction) followed by the statistics of the challenge:
// nodes checked, tasks, nodes fixed by each rule and by each kind of value
// correction, then what is left to the JOSM validator and to the mappers.
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
        text.AppendLine($"- Tasks (nodes with legacy tagging or mistyped values): {content.Tasks.Count}");
        text.AppendLine();

        AppendTable(text, "Fixed by the rule table", "Legacy tagging", content.RuleCounts);
        AppendTable(text, "Fixed against the documented values (MapRoulette only)", "Mistyped value", content.TokenFixCounts);
        AppendTable(text, "Not fixed: the replacement would overwrite a tag (fix by hand)", "Legacy tagging", content.BlockedCounts);
        AppendTable(text, "Not fixed: documented alternatives that cannot be combined", "Tags", content.ConflictCounts);
        AppendTable(text, "Not fixed: undocumented keys and values left (review by hand)", "Key or key=value", content.UndocumentedCounts);

        File.WriteAllText(path, text.ToString().ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
    }

    private static void AppendTable(StringBuilder text, string title, string header, IReadOnlyList<KeyValuePair<string, int>> counts)
    {
        if (counts.Count == 0)
        {
            return;
        }

        text.AppendLine($"### {title}");
        text.AppendLine();
        text.AppendLine($"| {header} | Nodes |");
        text.AppendLine("|---|---:|");
        foreach (var (label, count) in counts)
        {
            text.AppendLine($"| `{label.Replace("|", "\\|", StringComparison.Ordinal)}` | {count} |");
        }

        text.AppendLine();
    }
}
