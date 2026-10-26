// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Signalling.Rules;

// The hand-maintained part of the validator and of the MapRoulette challenge:
// how legacy tagging is replaced, and which documented tags must not be
// combined. Everything else (what is documented) comes from the wiki.
//
// Value replacements rename one value of a key, in place. The value is matched
// as a token, so the same entry also fixes a ";" separated list such as
// railway:signal:main:states=FR:CV;FR:M. An empty "to" drops the token (and
// the key with its last token):
//   { "key": "railway:signal:main", "from": "FR:CARRE", "to": "FR:C" }
//
// Tag replacements describe a structural change. "match" lists the tags that
// identify the legacy tagging: a value, "*" for any value, or null when the key
// must be absent. "set" adds or overwrites tags, "rename" moves a value to
// another key (for example ...:form to ...:marker:form) and "remove" deletes
// keys:
//   { "match": { "railway:signal:speed_limit": "FR:Z" },
//     "set": { "railway:signal:speed_limit": "FR:speed_indicator", ... },
//     "rename": { "railway:signal:speed_limit:form": "railway:signal:speed_limit:marker:form" } }
//
// A replacement never overwrites what it does not match: a tag it sets must be
// absent or already hold that value (or a legacy spelling of it, from the value
// replacements), and a key it renames to must be absent. Otherwise the legacy
// tagging is only reported, to be fixed by hand. An optional "note" is added
// to the message, for what the fix cannot do (a ref to add by hand...).
//
// "default" gives a value to a key left absent by the fix, typically the form
// of a board when the legacy tagging has none: a renamed value wins over it, and
// a key already set is kept:
//   { "rename": { "railway:signal:stop:form": "railway:signal:stop:conventional:form" },
//     "default": { "railway:signal:stop:conventional:form": "sign" } }
//
// "suggest" marks a replacement that guesses the right tagging from an
// incomplete one: the message asks "did you mean ...?" with the note, or else
// the tags it sets, and the fix applies them. A key to overwrite goes into
// "remove" as well as "set".
//
// "undocumented" marks a replacement whose legacy tagging is a mistake rather
// than a former tagging (FR alone, a truncated value...): the message says it
// "is not documented" instead of "is deprecated".
//
// A replacement with a "note" and nothing to fix (no set, rename, remove or
// default) only reports the legacy tagging, with the note telling what to do.
//
// "acceptedKeys" lists keys that the wiki page does not document but that are
// valid, documented on another page (railway:signal:catenary_mast). They are
// never reported as undocumented, with any value. A key that the wiki comes to
// document must leave the list (the generation fails otherwise).
//
// Conflicts list documented tags that are alternatives for the same sign and
// must not appear together; "note" tells how to choose:
//   { "tags": { "railway:signal:speed_limit:switch": "FR:chevron", "railway:signal:stop": "FR:chevron" },
//     "note": "..." }
//
// Redundancies list documented tags that are implied by the rest of the
// tagging: the validator warns (they are not legacy) and offers to remove them,
// and the MapRoulette challenge removes them with the legacy tagging, to keep
// the data lean. They use the shape of a tag replacement with "match",
// "remove" and an optional "note" only:
//   { "match": { "railway:crocodile": "yes", "railway:signal:main": "FR:C" },
//     "remove": [ "railway:crocodile" ], "note": "..." }
public sealed class RuleTable
{
    // A "match" value that accepts any value of the key.
    public const string AnyValue = "*";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public List<ValueReplacement> ValueReplacements { get; set; } = [];

    public List<TagReplacement> TagReplacements { get; set; } = [];

    public List<TagConflict> Conflicts { get; set; } = [];

    public List<TagReplacement> Redundancies { get; set; } = [];

    public List<string> AcceptedKeys { get; set; } = [];

    // Tags as "key=value + key=value", for messages and reports.
    public static string Describe(IEnumerable<KeyValuePair<string, string>> tags) =>
        string.Join(" + ", tags.Select(tag => $"{tag.Key}={tag.Value}"));

    // The value a replacement sets and its legacy spellings, which the value
    // replacements turn into it.
    public IReadOnlySet<string> AcceptedValues(string key, string value) =>
        ValueReplacements.Where(entry => entry.Key == key && entry.To == value)
            .Select(entry => entry.From)
            .Append(value)
            .ToHashSet(StringComparer.Ordinal);

    public static RuleTable Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<RuleTable>(stream, Options)
            ?? throw new InvalidDataException($"Empty rule table: {path}");
    }
}

public sealed class ValueReplacement
{
    public string Key { get; set; } = string.Empty;

    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;
}

public sealed class TagReplacement
{
    // Ordered so the generated selector and message follow the file.
    public Dictionary<string, string?> Match { get; set; } = [];

    public Dictionary<string, string> Set { get; set; } = [];

    public Dictionary<string, string> Rename { get; set; } = [];

    public List<string> Remove { get; set; } = [];

    public Dictionary<string, string> Default { get; set; } = [];

    public bool Suggest { get; set; }

    public bool Undocumented { get; set; }

    // Shown with the legacy tagging, for what is left to do by hand.
    public string? Note { get; set; }

    // True when the legacy tagging is matched by a single tag, so the rule
    // reports every occurrence of that tag.
    [JsonIgnore]
    public bool IsUnconditional => Match.Count == 1 && PresentTags.Any();

    // True when the replacement only reports the legacy tagging, with its note.
    [JsonIgnore]
    public bool IsReportOnly => Set.Count == 0 && Rename.Count == 0 && Remove.Count == 0 && Default.Count == 0;

    // Pairs of "match" that must be present (a null value means absent).
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, string>> PresentTags =>
        Match.Where(entry => entry.Value is not null).Select(entry => KeyValuePair.Create(entry.Key, entry.Value!));

    // The matched tags that the fix changes, which name the legacy tagging in
    // the messages; the others only give the context (all of them when the
    // replacement only reports). A key matched with any value is legacy when
    // the fix removes or renames it, not when it only overwrites it.
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, string>> LegacyTags
    {
        get
        {
            var changed = PresentTags.Where(tag => IsChanged(tag.Key)).ToList();
            return changed.Count > 0 ? changed : PresentTags;
        }
    }

    // Keys the replacement deals with when it matches: its matched, renamed and
    // removed keys. The validator reports nothing else about them on the node.
    [JsonIgnore]
    public IEnumerable<string> HandledKeys =>
        PresentTags.Select(tag => tag.Key).Concat(Rename.Keys).Concat(Remove).Distinct(StringComparer.Ordinal);

    // Tags set on a key the replacement does not match, remove or rename: the
    // key must be absent or already hold the value.
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, string>> GuardedSets => Set.Where(tag => !IsReplaced(tag.Key));

    // Rename targets the replacement does not match, remove or rename: the key
    // must be absent.
    [JsonIgnore]
    public IEnumerable<string> GuardedTargets => Rename.Values.Where(key => !IsReplaced(key));

    private bool IsReplaced(string key) => Match.ContainsKey(key) || Remove.Contains(key) || Rename.ContainsKey(key);

    private bool IsChanged(string key) =>
        Remove.Contains(key)
        || Rename.ContainsKey(key)
        || (Set.TryGetValue(key, out var value) && Match.GetValueOrDefault(key) is { } matched && matched != RuleTable.AnyValue && matched != value);
}

public sealed class TagConflict
{
    public Dictionary<string, string> Tags { get; set; } = [];

    public string? Note { get; set; }
}
