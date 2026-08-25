# French Railway Signalling

[![OpenStreetMap](https://img.shields.io/badge/OpenStreetMap-wiki-7ebc6f?logo=openstreetmap)](https://wiki.openstreetmap.org/wiki/OpenRailwayMap/Tagging_in_France)
[![JOSM Presets](https://img.shields.io/badge/JOSM-Presets-blue)](https://josm.openstreetmap.de/wiki/Presets)
[![Build & Deploy](https://github.com/noeldev/FrenchRailwaySignalling/actions/workflows/build-and-deploy-presets.yml/badge.svg)](https://github.com/noeldev/FrenchRailwaySignalling/actions/workflows/build-and-deploy-presets.yml)
[![XML](https://img.shields.io/badge/XML-✓-blueviolet)](https://github.com/noeldev/FrenchRailwaySignalling/blob/main/presets/French_Railway_Signalling.xml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL%203.0-yellow.svg)](https://www.gnu.org/licenses/gpl-3.0)

This repository serves as a **backup** of the pages, Lua modules, and templates I authored for the [OpenRailwayMap wiki](https://wiki.openstreetmap.org/wiki/OpenRailwayMap/Tagging_in_France), focusing on **French railway signalling**. It ensures long-term preservation of the documentation in case of unexpected deletions or inappropriate edits on the wiki platform.

## JOSM Presets

The repository includes **JOSM presets** specifically designed for mapping French railway signalling. These presets are aligned with the current revision of the wiki documentation.

### Installation

**Recommended method** - Install directly from JOSM:

1. Go to **Presets** → **Presets preferences...**
2. Search for **French Railway Signalling** in the **Available Presets** search box
3. Select the matching preset and click the right-pointing arrow to add it to **Active Presets**
4. Click **OK**

**Alternative method** - Manual installation:

⬇️ [Download the ZIP archive](https://noeldev.github.io/FrenchRailwaySignalling/French_Railway_Signalling_presets.zip)

This archive contains the latest preset XML files along with the required icon assets (SVG and PNG).

### taginfo project

A [taginfo project file](https://wiki.openstreetmap.org/wiki/Taginfo/Projects) is generated from the presets by the `TagInfoGen` tool and published at [`taginfo.json`](https://noeldev.github.io/FrenchRailwaySignalling/taginfo.json). It lets the French signalling tags, with their descriptions and icons, appear on [taginfo](https://taginfo.openstreetmap.org/). The presets are the single source of truth, so the file is regenerated automatically on every deployment and never needs manual editing.

## Tools

The `tools/` directory holds C# (.NET 10) console applications that support the presets and the wiki backup. The signalling-specific tools and their shared libraries live together under `tools/Signalling/` (one solution); `SvgSquarer` and `WikiBackup` are independent.

- **SignalAudit** - Validates `French_Railway_Signalling.xml` and audits it against related sources. Offline, it checks the file against the JOSM tagging preset schema (the schema location is read from the preset file and downloaded automatically), verifies chunk definitions and references, confirms every icon exists on disk with exact path casing (so nothing breaks on the case-sensitive GitHub Pages host), resolves the wiki link of each item either inline or through referenced chunks, and checks structure, menu-path uniqueness and `match_expression` usage. With `--wiki` it additionally verifies over the network that wiki links and anchors resolve, and that the preset's tags match the wiki specification (the source of truth). With `--yaml` it checks that every tag the preset emits is handled by the [OpenRailwayMap-vector](https://github.com/hiddewie/OpenRailwayMap-vector) map. Results can be written as a transcript (`--log`) and a structured JSON report (`--report-json`).
- **SvgSquarer** - Normalizes the preset SVG icons to a square `viewBox` so they render consistently in JOSM and on taginfo.
- **TagInfoGen** - Generates the taginfo project file from the presets, published as `taginfo.json`.
- **WikiBackup** - Downloads the wiki pages, Lua modules, and templates into `wiki/backup/` for long-term preservation.

The shared libraries under `tools/Signalling/libs/` are reused by SignalAudit and TagInfoGen: `Signalling.Core` (tag-rule vocabulary and local/remote source resolution), `Signalling.Presets` (JOSM preset reader), `Signalling.OrmVector` (ORM-vector YAML reader), `Signalling.Wiki` (OSM wiki reader), and `Signalling.Sync` (the tag-rule comparator behind the cross-source audits).

## Repository Structure

```
FrenchRailwaySignalling/
├── .github/
│   ├── pages/            # GitHub Pages static files
│   │   └── index.html    # Landing page
│   └── workflows/        # GitHub Actions workflows
│       ├── backup-wiki-pages.yml         # Manual backup of wiki pages
│       └── build-and-deploy-presets.yml  # Build and deploy presets (+ taginfo.json)
├── presets/              # JOSM preset files and assets
│   ├── French_Railway_Signalling.xml     # Preset (single source of truth)
│   └── icons/            # SVG icons (boards, boxes, plates, signals, signs) and SNCF_logo.png
├── tools/
│   ├── Signalling/       # Signalling toolset (single solution)
│   │   ├── SignalAudit/  # Preset validator + cross-source audit (ORM map, OSM wiki)
│   │   ├── TagInfoGen/   # taginfo project file generator
│   │   └── libs/         # Shared libraries
│   │       ├── Signalling.Core/      # Tag-rule vocabulary + source resolvers
│   │       ├── Signalling.Presets/   # JOSM preset reader
│   │       ├── Signalling.OrmVector/ # ORM-vector YAML reader
│   │       ├── Signalling.Wiki/      # OSM wiki reader
│   │       └── Signalling.Sync/      # Tag-rule comparator
│   ├── SvgSquarer/       # SVG viewBox normalizer
│   └── WikiBackup/       # Wiki backup tool
└── wiki/
    ├── backup/           # Auto-generated wiki backups
    └── draft/            # Initial content
```

## Status

🚧 **Work in Progress**

## Related Links

- 🌐 [Main Wiki Page – Tagging in France (EN)](https://wiki.openstreetmap.org/wiki/OpenRailwayMap/Tagging_in_France)
- 🗺️ [OpenRailwayMap](https://www.openrailwaymap.app)
- 🚦 [SNCF Signalisation Permanente](https://sncf-sigmap.netlify.app) – interactive map of French railway signalling based on SNCF open data

## License

- **Wiki Content**: Available under the same license as OpenStreetMap wiki content
- **JOSM Presets**: GPL-3.0
- **Tools** (`SignalAudit`, `SvgSquarer`, `TagInfoGen`, `WikiBackup`) and the shared `Signalling.*` libraries: GPL-3.0
- **Icons**: Original creations or adaptations from [Wikimedia Commons](https://commons.wikimedia.org) and [Nicolas Wurtz's signalisation-rfn-svg project](https://github.com/nicolaswurtz/signalisation-rfn-svg)
```
