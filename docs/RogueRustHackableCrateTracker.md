# RogueRustHackableCrateTracker

> Tracks hackable crates, hackers, looters, map grids, world metadata and Discord notifications.

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustHackableCrateTracker.cs) · [Back to plugin catalogue](../README.md)

## Features

- Reports crate hack attempts, accepted hack starts and completion.
- Tracks the original/known hacker and first actual looter.
- Includes Rust map grid, optional exact XYZ and optional world seed/size metadata.
- In-game reporting with optional permission restriction.
- Console reporting.
- Built-in Discord webhook embeds/reporting without requiring a Discord plugin.
- Optional map URL template using `{grid}`, `{x}`, `{z}`, `{worldsize}` and `{seed}`.
- Compatibility alias for older/custom crate-hack hook providers.

## Permissions

- `roguerusthackablecratetracker.use` — receives restricted in-game reports when restriction is enabled.
- `roguerusthackablecratetracker.admin` — reserved administrative permission for plugin administration/current command surface.

## Commands

No public RogueCommand handlers are defined in the current revision. Tracking is event-driven.

## Configuration

Configuration file: `config/RogueRustHackableCrateTracker.json`

### Reporting Settings

| Setting | Default |
| --- | --- |
| Enable In-Game Reporting | `true` |
| Only Send In-Game Reports to Permitted Players | `false` |
| Report Hack Attempts | `true` |
| Enable Console Reporting | `true` |
| Report Hack Completion | `true` |
| Report First Actual Looter | `true` |

### Discord Settings

| Setting | Default |
| --- | --- |
| Enabled | `false` |
| Webhook URL | empty |
| Map URL Template (`{grid}`, `{x}`, `{z}`, `{worldsize}`, `{seed}`) | empty |

When Discord is enabled with an invalid/empty webhook URL, the plugin logs a warning rather than treating Discord as a hard dependency.

### Location Settings

- `Include Exact XYZ Coordinates`: `true`
- `Include World Metadata`: `true`

### Default configuration

```json
{
  "Reporting Settings": {
    "Enable In-Game Reporting": true,
    "Only Send In-Game Reports to Permitted Players": false,
    "Report Hack Attempts": true,
    "Enable Console Reporting": true,
    "Report Hack Completion": true,
    "Report First Actual Looter": true
  },
  "Discord Settings": {
    "Enabled": false,
    "Webhook URL": "",
    "Map URL Template ({grid}, {x}, {z}, {worldsize}, {seed})": ""
  },
  "Location Settings": {
    "Include Exact XYZ Coordinates": true,
    "Include World Metadata": true
  },
  "Version (DO NOT CHANGE)": "2.1.0"
}
```

## Data and localization

Persistent custom data: **none**.  
Custom language files: **none** in the current revision.

Crate state is runtime-only and cleared on unload.

## Migration

Legacy flat settings such as `Discord webhook URL`, `Enable in-game reporting`, `Configuration version` and related keys are migrated into the grouped Reporting/Discord/Location layout.

## Performance notes

- Crates are keyed by `NetworkableId` for direct lookup.
- Hack-attempt spam is debounced per player for two seconds and periodically cleaned.
- Hack completion uses the authoritative current hook with a delayed fallback for older framework builds.
- No polling loop is required for normal tracking.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustHackableCrateTracker.cs` in the plugin directory, then configure reporting/Discord options. Replace the `.cs` file for updates; legacy flat configuration is migrated automatically.
