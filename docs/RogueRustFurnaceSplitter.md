# RogueRustFurnaceSplitter

> Optimized RogueRust furnace splitting with automatic fuel management, configurable stack distribution, ETA display and responsive UI.

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustFurnaceSplitter.cs) · [Back to plugin catalogue](../README.md)

## Features

- Automatically splits cookable input across furnace/oven slots.
- Calculates fuel requirement and smelting ETA.
- Per-oven enable state and fuel multiplier.
- Per-player enabled state and preferred total stack counts.
- Optional persistent player preferences.
- Responsive furnace UI with queued/batched redraws.
- Legacy FurnaceSplitter permission/config/data compatibility.
- Optional UI scaling integration.

## Dependencies

Required: RogueRust DLL.  
Optional: `UIScaleManager`.

## Permissions

- `roguerustfurnacesplitter.use` — use Furnace Splitter.
- Legacy `furnacesplitter.use` is retained for compatibility.

## Commands

- `/fs` — primary Furnace Splitter player control.
- `furnacesplitter.enabled` — toggle saved enabled state.
- `furnacesplitter.totalstacks` — change preferred stack count.
- `furnacesplitter.trim` — stack trimming/control action used by the UI/command layer.

## Configuration

Configuration file: `config/RogueRustFurnaceSplitter.json`

The generated configuration includes general behaviour, UI behaviour and an oven dictionary. At server initialization the plugin scans Rust oven prefabs that support by-products and adds missing oven definitions to configuration.

Each oven definition supports at least:

- `enabled` — whether that specific oven rule is enabled.
- `fuelMultiplier` — multiplier applied to calculated fuel requirement.

A global/default oven rule can be used while individual oven entries override behaviour. The source-generated oven dictionary should be treated as authoritative because available oven prefabs can change with Rust updates.

## Data

Player preferences are stored at:

`RogueRustFurnaceSplitter/player-options`

The stored model contains each player's enabled state and total-stack preference per oven. Data is loaded/saved only when player-data persistence is enabled. Saves are debounced during runtime and written on server save/unload.

## Localization

`lang/<language>/RogueRust/RogueRustFurnaceSplitter/messages.json`

Legacy language/data layouts are migrated where supported.

## UI and performance notes

- Furnace UI refreshes are queued and processed in batches of up to 12 ovens rather than redrawing every affected oven immediately.
- Duplicate queued oven updates are coalesced with a `HashSet`.
- Oven definitions and initial stack counts are discovered once at initialization.
- ETA/fuel calculation uses the current oven's smelt speed, fuel type and configured fuel multiplier.
- UI is destroyed cleanly on disconnect/unload.

## Installation

1. Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
2. Place `RogueRustFurnaceSplitter.cs` in the plugin directory.
3. Optionally install UIScaleManager.
4. Grant `roguerustfurnacesplitter.use`.
5. Review generated oven configuration after first load.

## Updating

Replace the `.cs` file and retain config/data. Newly introduced Rust oven prefabs are added to configuration automatically on initialization.
