// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using SignalBot.Osm;

namespace SignalBot.Core;

// The mappers of a country who asked for their signals to be fixed, matched
// by uid since a display name can change. Opting in adds a mapper, opting out
// removes them.
public sealed class OptedInMappers(IEnumerable<OsmUser> users)
{
    private readonly List<OsmUser> _users = [.. users];

    public IReadOnlyList<OsmUser> Users => _users;

    public IReadOnlySet<long> Uids => _users.Select(user => user.Uid).ToHashSet();

    // False when the mapper was already in the list; their name is updated.
    public bool Add(OsmUser user)
    {
        var index = _users.FindIndex(entry => entry.Uid == user.Uid);
        if (index >= 0)
        {
            _users[index] = user;
            return false;
        }

        _users.Add(user);
        return true;
    }

    // The mapper removed, found by display name (ignoring case), or null.
    public OsmUser? Remove(string name) => Remove(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // The mapper removed, found by uid, or null.
    public OsmUser? Remove(long uid) => Remove(entry => entry.Uid == uid);

    private OsmUser? Remove(Predicate<OsmUser> match)
    {
        var user = _users.Find(match);
        if (user is not null)
        {
            _users.Remove(user);
        }

        return user;
    }
}
