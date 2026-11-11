// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using RailwayMapBot.Reporting;

namespace RailwayMapBot.Tests;

public sealed class TagChangesTests
{
    [Fact]
    public void Describe_marks_added_removed_and_changed_tags()
    {
        var before = TestBot.Tags(("railway", "signal"), ("railway:signal:main", "FR:CARRE"), ("railway:crocodile", "yes"));
        var after = TestBot.Tags(("railway", "signal"), ("railway:signal:main", "FR:C"), ("railway:signal:main:form", "light"));

        var changes = TagChanges.Describe(before, after).ToList();

        Assert.Equal(["- railway:crocodile=yes", "~ railway:signal:main: FR:CARRE -> FR:C", "+ railway:signal:main:form=light"], changes);
    }
}
