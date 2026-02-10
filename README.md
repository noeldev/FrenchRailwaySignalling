# French Railway Signalling

[![OpenStreetMap](https://img.shields.io/badge/OpenStreetMap-wiki-7ebc6f?logo=openstreetmap)](https://wiki.openstreetmap.org/wiki/OpenRailwayMap/Tagging_in_France)
[![JOSM Presets](https://img.shields.io/badge/JOSM-Presets-blue)](https://josm.openstreetmap.de/wiki/Presets)
[![Build & Deploy](https://github.com/noeldev/FrenchRailwaySignalling/actions/workflows/build-and-deploy-presets.yml/badge.svg)](https://github.com/noeldev/FrenchRailwaySignalling/actions/workflows/build-and-deploy-presets.yml)
[![XML](https://img.shields.io/badge/XML-✓-blueviolet)](https://github.com/noeldev/FrenchRailwaySignalling/blob/main/presets/French_Railway_Signalling.xml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

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

## Repository Structure

```
FrenchRailwaySignalling/
├── .github/
│   ├── pages/            # GitHub Pages static files
│   │   └── index.html    # Landing page
│   └── workflows/        # GitHub Actions workflows
│       ├── backup-wiki-pages.yml         # Manual backup of wiki pages
│       └── build-and-deploy-presets.yml  # Build and deploy presets
├── presets/              # JOSM preset files and assets
│   ├── French_Railway_Signalling.xml
│   ├── icons/            # SVG and PNG icons
│   └── font/             # SNCF font used by some icons
├── tools/
│   └── WikiBackup/       # C# wiki backup tool
└── wiki/
    ├── backup/           # Auto-generated wiki backups
    └── draft/            # Initial content
```

## Status

🚧 **Work in Progress**

## Related Links

- 🌐 [Main Wiki Page – Tagging in France (EN)](https://wiki.openstreetmap.org/wiki/OpenRailwayMap/Tagging_in_France)
- 🗺️ [OpenRailwayMap](https://www.openrailwaymap.app)

## License

- **Wiki Content**: Available under the same license as OpenStreetMap wiki content
- **JOSM Presets**: MIT License
- **Icons**: Original creations or adaptations from [Wikimedia Commons](https://commons.wikimedia.org) and [Nicolas Wurtz's signalisation-rfn-svg project](https://github.com/nicolaswurtz/signalisation-rfn-svg)
