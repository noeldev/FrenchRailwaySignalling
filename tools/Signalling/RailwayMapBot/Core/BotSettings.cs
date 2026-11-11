// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noël Danjou

using Signalling.Schema;

namespace RailwayMapBot.Core;

// Configuration bound from appsettings.json, an optional --settings file
// (another country, another scheme) and the settings of the operator in the
// user profile (see SettingsLoader). Nothing about a country or a server lives
// in the code. Property names are matched to the JSON keys case-insensitively.
public sealed class BotSettings
{
    // Wiki pages documenting the scheme, the same as ValidatorGen's. The bot
    // only reads their reviewed local copy.
    public WikiSettings Wiki { get; set; } = new();

    // Only keys under this prefix are checked, for example "railway:signal:".
    public string KeyPrefix { get; set; } = string.Empty;

    // Prefix of the documented values, for example "FR" for "FR:C".
    public string CountryPrefix { get; set; } = string.Empty;

    // Rule table shared with ValidatorGen and MapRouletteGen, relative to the
    // repository root.
    public string Rules { get; set; } = string.Empty;

    // Folder of the run reports and journals, relative to the repository root.
    public string Output { get; set; } = string.Empty;

    // Where to reach the operator, sent in the User-Agent header.
    public string Contact { get; set; } = string.Empty;

    // Tags a node must carry to be edited, checked on the fresh data.
    public Dictionary<string, string> Scope { get; set; } = [];

    // Server used when --server is not given: the sandbox, never production.
    public string DefaultServer { get; set; } = string.Empty;

    public Dictionary<string, ServerSettings> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public OverpassSettings Overpass { get; set; } = new();

    // Areas the signals are read and grouped by, one changeset series each:
    // the areas of a whole run, when none is given.
    public List<AreaSettings> Areas { get; set; } = [];

    // Kinds of area codes the operator can give (--area), each recognized by
    // its pattern: the configured areas, and others such as smaller ones.
    public List<AreaKindSettings> AreaKinds { get; set; } = [];

    public ChangesetSettings Changeset { get; set; } = new();

    public PolicySettings Policy { get; set; } = new();

    public OptOutSettings OptOut { get; set; } = new();

    public LimitSettings Limits { get; set; } = new();
}

// One OSM server: the production one or the development sandbox.
public sealed class ServerSettings
{
    // API host, for example https://api.openstreetmap.org.
    public string ApiUrl { get; set; } = string.Empty;

    // Website host, for the OAuth 2 pages and the links of the report.
    public string WebUrl { get; set; } = string.Empty;

    // Optional changeset review link of the report, with an {id} placeholder.
    public string ReviewUrl { get; set; } = string.Empty;

    // Display name (not the e-mail address) of the dedicated account the bot
    // must be logged in with; any other one is refused before the first
    // write. Set by login, in the settings of the operator.
    public string Account { get; set; } = string.Empty;

    // Client ID of the OAuth 2 application registered on that account. Set by
    // login, in the settings of the operator.
    public string ClientId { get; set; } = string.Empty;

    // True on a sandbox only: lets the seed command create test nodes.
    public bool AllowSeeding { get; set; }

    // SHA-256 of the rule table the community discussion was held on. When
    // set, apply refuses any other rule table: a change of the corrections
    // must be documented and discussed first. Required outside a sandbox. Set
    // by approve, in the settings of the operator.
    public string ApprovedRulesSha256 { get; set; } = string.Empty;

    // A node edited less than this long ago is left alone, since its mapper
    // may still be working on it.
    public int QuietPeriodHours { get; set; }
}

public sealed class OverpassSettings
{
    public string Endpoint { get; set; } = string.Empty;

    // Pause between two area queries.
    public int DelaySeconds { get; set; }

    // Overpass QL query, one line per entry; {area} is replaced by the
    // selector of the area (see AreaKindSettings). It must output the nodes
    // with their metadata (out meta), and may output the area (.scope out
    // tags;) to give its name.
    public List<string> Query { get; set; } = [];
}

public sealed class AreaKindSettings
{
    // What the code designates, shown to the operator, for example "region".
    public string Name { get; set; } = string.Empty;

    // Regular expression a code of this kind matches (whole code).
    public string Pattern { get; set; } = string.Empty;

    // Overpass area statement put in place of {area} in the query; {code} is
    // replaced by the code.
    public string Selector { get; set; } = string.Empty;

    // Codes shown as examples to the operator.
    public string Examples { get; set; } = string.Empty;
}

public sealed class AreaSettings
{
    // Code of the area, as the operator types it (see AreaKindSettings).
    public string Code { get; set; } = string.Empty;

    // Name written in the changeset comments and in the report.
    public string Name { get; set; } = string.Empty;
}

// Changeset comments, in the language of the mapped country, and the tags
// every changeset carries. Placeholders: {area}, {batch} and {tool}.
public sealed class ChangesetSettings
{
    public string FixComment { get; set; } = string.Empty;

    public string RevertComment { get; set; } = string.Empty;

    public string SeedComment { get; set; } = string.Empty;

    public Dictionary<string, string> Tags { get; set; } = [];
}

// What the bot leaves to a human, through MapRoulette or JOSM.
public sealed class PolicySettings
{
    // A fix that guesses the tagging from an incomplete one ("suggest").
    public bool SkipSuggestions { get; set; } = true;

    // A fix whose note asks the mapper for a complement (a caption...).
    public bool SkipIncomplete { get; set; } = true;

    // Mistyped values that lead to exactly one documented value.
    public bool FixMistypedValues { get; set; } = true;
}

// Mappers and nodes the bot must leave untouched, on request.
public sealed class OptOutSettings
{
    // Nodes one of these users ever edited are skipped (the history of each
    // node to edit is read when the list is not empty).
    public List<string> Users { get; set; } = [];

    public List<long> Nodes { get; set; } = [];
}

public sealed class LimitSettings
{
    // Edits of one run when --limit is not given.
    public int MaxEditsPerRun { get; set; }

    public int MaxNodesPerChangeset { get; set; }

    // Largest width and height of the area of one changeset, in degrees. The
    // OSM server refuses wider changesets from a new account (about 1.5
    // degrees at first), so the nodes of an area are batched by squares of
    // this size.
    public double MaxChangesetSpanDegrees { get; set; } = 1.0;

    // Node ids of one read request (the URL length is limited).
    public int NodesPerRequest { get; set; }

    public int DelayBetweenChangesetsSeconds { get; set; }

    // Times a batch is read and decided again after a version conflict.
    public int ConflictRetries { get; set; }

    // Times a failed read is sent again. A write is never sent twice.
    public int ReadRetries { get; set; }

    public int RetryDelaySeconds { get; set; }

    // Longest wait accepted when the server limits the edit rate; beyond it
    // the run stops and is resumed later.
    public int MaxRateLimitWaitSeconds { get; set; }

    public int RequestTimeoutSeconds { get; set; }
}
