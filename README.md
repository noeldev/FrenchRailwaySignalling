# French Railway Signalling

This repository serves as a **backup** of the pages, Lua modules, and templates I authored for the [OpenRailwayMap wiki](https://wiki.openstreetmap.org/wiki/OpenRailwayMap/Tagging_in_France), focusing on **French railway signalling**. It ensures long-term preservation of the documentation in case of unexpected deletions or inappropriate edits on the wiki platform.

## JOSM Presets

The repository includes **JOSM presets** specifically designed for mapping French railway signalling. These presets are aligned with the current revision of the wiki documentation.

### Installation

**Recommended method** - Install directly from JOSM:

1. Go to **Presets** → **Presets Preferences** → **Tagging Presets**
2. Search for **French Railway Signalling** in the **Available Presets** search box
3. Select the matching preset and click the right-pointing arrow to add it to **Active Presets**
4. Click **OK**

**Alternative method** - Manual installation:

⬇️ [Download the ZIP archive](https://github.com/noeldev/FrenchRailwaySignalling/releases/latest/download/French_Railway_Signalling_presets.zip)

This archive contains the latest preset XML files along with the required icon assets (SVG and PNG).

## Repository Structure

```
FrenchRailwaySignalling/
├── .github/workflows/    # GitHub Actions workflows
│   ├── generate-presets-archive.yml    # Auto-generates preset archive
│   └── backup-wiki-pages.yml           # Manual backup of wiki pages
├── presets/              # JOSM preset files and assets
│   ├── French_Railway_Signalling.xml
│   ├── icons/            # SVG and PNG icons
│   └── font/             # SNCF font used by some icons
├── tools/
│   └── WikiBackup/       # C# wiki backup tool
├── wiki/
│   ├── backup/           # Auto-generated wiki backups
│   └── draft/            # Initial content
└── French_Railway_Signalling_presets.zip  # Auto-generated preset archive
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
