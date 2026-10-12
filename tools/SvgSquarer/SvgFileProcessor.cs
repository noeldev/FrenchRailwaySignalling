// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

namespace SvgSquarer;

internal enum ProcessStatus
{
    Squared,
    Unsquared,
    Cleaned,
    Unchanged,
    Warning
}

internal readonly record struct ProcessResult(ProcessStatus Status, string Detail);

// Squares or unsquares one SVG in place through IconSquarer. The file is
// rewritten only when its text actually changes, so a second run leaves it
// untouched.
internal static class SvgFileProcessor
{
    public static ProcessResult Process(string path, bool unsquare, bool dryRun)
    {
        var text = Utf8TextFile.Read(File.ReadAllBytes(path), out var hasBom);
        var result = unsquare ? IconSquarer.Unsquare(text) : IconSquarer.Square(text);

        var (status, detail) = result.Status switch
        {
            SquareStatus.Squared => (ProcessStatus.Squared, result.Detail),
            SquareStatus.Unsquared => (ProcessStatus.Unsquared, result.Detail),
            SquareStatus.AlreadySquare when result.Text != text => (ProcessStatus.Cleaned, "removed width/height/preserveAspectRatio"),
            SquareStatus.AlreadySquare or SquareStatus.NotSquared => (ProcessStatus.Unchanged, result.Detail),
            _ => (ProcessStatus.Warning, result.Detail)
        };

        if (!dryRun && status is ProcessStatus.Squared or ProcessStatus.Unsquared or ProcessStatus.Cleaned)
        {
            Utf8TextFile.Write(path, result.Text, hasBom);
        }

        return new ProcessResult(status, detail);
    }
}
