// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text;

namespace ValidatorGen.MapCss;

// One MapCSS rule: an optional comment, its selectors (several selectors share
// the declarations) and the declarations, already spelled.
public sealed record MapCssRule(string? Comment, IReadOnlyList<string> Selectors, IReadOnlyList<string> Declarations)
{
    public void Render(StringBuilder output, string newLine)
    {
        if (Comment is not null)
        {
            output.Append("/* ").Append(Comment).Append(" */").Append(newLine);
        }

        output.Append(string.Join("," + newLine, Selectors)).Append(" {").Append(newLine);
        foreach (var declaration in Declarations)
        {
            output.Append("  ").Append(declaration).Append(';').Append(newLine);
        }

        output.Append('}').Append(newLine).Append(newLine);
    }
}
