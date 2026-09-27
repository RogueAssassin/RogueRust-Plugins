# RogueRust Rando Spawns

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Random respawn system with biome weighting, topology/zone protection, cached spawn generation and compatibility hooks.

## Features

- Biome-aware random respawns
- Cached spawn generation
- Slope and building-distance validation
- Topology blocking
- Optional ZoneManager zone blocking
- Minimum online-player requirements by biome
- Admin spawn visualization/regeneration commands

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerustrandospawns.admin` — administrative spawn commands.

## Commands

- `showspawns` — visualizes cached spawn points.
- `randospawns.regenerate` — regenerates the spawn cache.

## Configuration

The current revision groups its configuration into:

- `Generation Options`
- `Spawn Options`

Optional integration: `ZoneManager` for blocked zone IDs.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustRandoSpawns.cs](../plugins/RogueRustRandoSpawns.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustRandoSpawns.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
