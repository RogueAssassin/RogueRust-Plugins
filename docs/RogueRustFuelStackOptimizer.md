# RogueRust Fuel Stack Optimizer

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Optimises dedicated Rust fuel containers through cached discovery using native Oxide/Carbon hooks. It is designed to change fuel stack limits without repeatedly scanning ordinary inventories, with batching and cached reflection plans to keep server overhead controlled.

## Features

- Dedicated fuel-container discovery and targeting
- Global fuel stack maximum
- Recognised fuel item short-name list
- Container-kind filtering
- Per-entity overrides by NetID, short prefab name or full prefab path
- Owner whitelist and blacklist filters
- Batched discovery and stack updates
- Optional timed rescans
- Startup audit and fuel/container diagnostics
- Automatic migration from the legacy flat configuration layout

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

No additional plugin dependency is required by the current revision.

## Permissions

- `roguerustfuelstackoptimizer.admin` — grants access to administrative diagnostics and control commands.

Administrators can also use supported administrative commands through native admin access where the plugin permits it.

## Commands

### `fuelstack.status`

Displays the current optimizer status and tracked-container summary.

**Permission:** `roguerustfuelstackoptimizer.admin`

### `fuelstack.audit`

Produces a detailed audit of discovered fuel containers and optimizer decisions.

**Permission:** `roguerustfuelstackoptimizer.admin`

### `fuelstack.fuels`

Displays recognised fuel information used by the optimizer.

**Permission:** `roguerustfuelstackoptimizer.admin`

### `fuelstack.rescan`

Requests a new full discovery scan so eligible containers can be rediscovered and updated.

### `fuelstackaudit`

Legacy/alternate audit command retained by the current source revision.

**Permission:** `roguerustfuelstackoptimizer.admin`

## Configuration

The settings are stored in `RogueRustFuelStackOptimizer.json` in the server configuration directory.

### General Settings

- **Global Max Fuel Stack Size** — maximum stack size applied to recognised fuel in eligible containers. Default: `1000`.
- **Recognised Fuel Item Short Names** — item short names treated as fuel. Defaults to `lowgradefuel`, `diesel_barrel`, `wood`, and `crude.oil`.

### Container Settings

- **Only Modify Dedicated Fuel Containers** — when `true`, limits changes to containers identified as dedicated fuel storage. Default: `true`.
- **Enabled Container Kinds** — controls which detected categories are eligible. Defaults include Generator, Quarry, Excavator, Vehicle, Light and Other.
- **Per Entity Overrides by NetID** — assigns a specific maximum stack to one entity network ID.
- **Per Entity Overrides by Short Prefab Name** — assigns a maximum by entity short prefab name.
- **Per Entity Overrides by Full Prefab Path** — assigns a maximum by full prefab path.

### Owner Filter Settings

- **Whitelist Owners by Steam ID or current name (empty = all)** — when empty, all owners are eligible. Add Steam IDs or current player names to limit processing to those owners.
- **Blacklist Owners by Steam ID or current name** — excludes matching owners from processing.

### Performance Settings

- **Enable Batch Processing** — spreads stack updates across ticks. Default: `true`.
- **Discovery Entities per Tick** — number of entities examined per discovery batch. Default: `40`; the plugin constrains this to `5–250`.
- **Stack Updates per Tick** — number of eligible container updates per batch. Default: `25`; constrained to `1–250`.
- **Rescan Interval in Seconds (0 = disabled)** — periodic full rescan interval. Default: `0`. Positive values are constrained to `60–86400` seconds.

### Developer Settings

- **Log Detailed Audit on Startup** — prints the detailed audit after the initial discovery scan. Default: `false`.

## Default Configuration

```json
{
  "General Settings": {
    "Global Max Fuel Stack Size": 1000,
    "Recognised Fuel Item Short Names": [
      "lowgradefuel",
      "diesel_barrel",
      "wood",
      "crude.oil"
    ]
  },
  "Container Settings": {
    "Only Modify Dedicated Fuel Containers": true,
    "Enabled Container Kinds": [
      "Generator",
      "Quarry",
      "Excavator",
      "Vehicle",
      "Light",
      "Other"
    ],
    "Per Entity Overrides by NetID (NetID : MaxStack)": {},
    "Per Entity Overrides by Short Prefab Name (Name : MaxStack)": {},
    "Per Entity Overrides by Full Prefab Path (Prefab : MaxStack)": {}
  },
  "Owner Filter Settings": {
    "Whitelist Owners by Steam ID or current name (empty = all)": [],
    "Blacklist Owners by Steam ID or current name": []
  },
  "Performance Settings": {
    "Enable Batch Processing": true,
    "Discovery Entities per Tick": 40,
    "Stack Updates per Tick": 25,
    "Rescan Interval in Seconds (0 = disabled)": 0.0
  },
  "Developer Settings": {
    "Log Detailed Audit on Startup": false
  },
  "Version (DO NOT CHANGE)": "2.1.0"
}
```

## Configuration Migration

Older flat configurations are detected automatically. The plugin migrates those values into the grouped RogueRust configuration layout and writes the current configuration version back to disk.

## Stored Data

This plugin does not require a persistent data file in the current revision. Runtime discovery state and caches are rebuilt from server entities.

## Performance Notes

Discovery is cached by entity type and can be processed in batches. Ordinary entity types that do not resemble fuel-storage entities are skipped early, reducing unnecessary reflection and inventory work. Entity spawn and kill hooks keep the tracked set current between full scans.

For large servers, keep batch processing enabled and increase the discovery/apply batch sizes only after observing server performance.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download [RogueRustFuelStackOptimizer.cs](../plugins/RogueRustFuelStackOptimizer.cs).
4. Place it in your framework's plugins directory.
5. Allow the framework to compile and load the plugin.
6. Review `RogueRustFuelStackOptimizer.json` and grant `roguerustfuelstackoptimizer.admin` only to trusted administrators who need diagnostics/control access.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is migrated where supported by the plugin. Do not manually alter `Version (DO NOT CHANGE)`.

## Source

[View the plugin source](../plugins/RogueRustFuelStackOptimizer.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
