// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValidatorGen.Rules;

// The hand-maintained part of the validator: how legacy tagging is replaced,
// and which documented tags must not be combined. Everything else (what is
// documented) comes from the wiki.
//
// Value replacements rename one value of a key, in place. The value is matched
// as a token, so the same entry also fixes a ";" separated list such as
// railway:signal:main:states=FR:CV;FR:M:
//   { "key": "railway:signal:main", "from": "FR:CARRE", "to": "FR:C" }
//
// Tag replacements describe a structural change. "match" lists the tags that
// identify the legacy tagging (a null value means the key must be absent);
// "set" adds or overwrites tags, "rename" moves a value to another key (for
// example ...:form to ...:marker:form) and "remove" deletes keys:
//   { "match": { "railway:signal:speed_limit": "FR:Z" },
//     "set": { "railway:signal:speed_limit": "FR:speed_indicator", ... },
//     "rename": { "railway:signal:speed_limit:form": "railway:signal:speed_limit:marker:form" } }
//
// Conflicts list documented tags that are alternatives for the same sign and
// must not appear together; "note" tells how to choose:
//   { "tags": { "railway:signal:speed_limit:switch": "FR:chevron", "railway:signal:stop": "FR:chevron" },
//     "note": "..." }
public sealed class RuleTable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public List<ValueReplacement> ValueReplacements { get; set; } = [];

    public List<TagReplacement> TagReplacements { get; set; } = [];

    public List<TagConflict> Conflicts { get; set; } = [];

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

    // Pairs of "match" that must be present (a null value means absent).
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, string>> PresentTags =>
        Match.Where(entry => entry.Value is not null).Select(entry => KeyValuePair.Create(entry.Key, entry.Value!));
}

public sealed class TagConflict
{
    public Dictionary<string, string> Tags { get; set; } = [];

    public string? Note { get; set; }
}
