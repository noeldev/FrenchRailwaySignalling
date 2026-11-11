// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Rules;
using static ValidatorGen.MapCss.MapCssSyntax;

namespace ValidatorGen.MapCss;

// The MapCSS eval expression of a tag template, which fills it the way
// TagTemplate.Fill does. A number placeholder ({key|n}) is truncated by text,
// not by arithmetic, so no rounding can change a digit:
// 1. "." and n zeros are appended when the value has no decimal point, else
//    only the zeros ("223" -> "223.0", "223.799" -> "223.7990");
// 2. regexp_match keeps the sign, the integer and the first n decimals;
// 3. the sign is dropped when every kept digit is zero, and the decimals are
//    written after a comma.
// The rule selector must ensure the value is a plain number
// (TagTemplate.NumberPattern), so the match always succeeds. The patterns
// span the whole string, so they give the same result whether the string is
// matched as a whole or searched.
public static class TemplateExpression
{
    public static string Build(TagTemplate template) =>
        $"concat({string.Join(", ", template.Parts.Select(Part))})";

    private static string Part(TemplatePart part) => part switch
    {
        { Literal: { } literal } => Quote(literal),
        { Decimals: { } decimals } => Number($"tag({Quote(part.Key!)})", decimals),
        _ => $"tag({Quote(part.Key!)})",
    };

    private static string Number(string value, int decimals)
    {
        if (decimals == 0)
        {
            var whole = $"regexp_match({Quote(@"^(-?)([0-9]+)(\.[0-9]*)?$")}, {value})";
            return $"concat({Sign(whole, $"get({whole}, 2)")}, get({whole}, 2))";
        }

        var padded = $"concat({value}, cond(regexp_test({Quote(@"^.*\..*$")}, {value}), \"\", \".\"), {Quote(new string('0', decimals))})";
        var match = $"regexp_match({Quote($@"^(-?)([0-9]+)\.([0-9]{{{decimals}}})[0-9]*$")}, {padded})";
        return $"concat({Sign(match, $"concat(get({match}, 2), get({match}, 3))")}, get({match}, 2), \",\", get({match}, 3))";
    }

    // The captured minus sign, or nothing when the digits are all zero.
    private static string Sign(string match, string digits) =>
        $"cond(regexp_test({Quote("^0+$")}, {digits}), \"\", get({match}, 1))";
}
