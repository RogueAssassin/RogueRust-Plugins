# RogueRustRandoSpawns

> RogueRust-powered random respawn system with biome weighting, topology/zone protection, cached spawn generation and compatibility hooks.

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustRandoSpawns.cs) · [Back to plugin catalogue](../README.md)

## Features

- Random cached respawn points generated across the playable map radius.
- Biome-specific enable state and minimum-online-player thresholds.
- Terrain slope, topology, building/deployable distance and zone validation.
- Persistent spawn cache tied to world seed and world size.
- Automatic regeneration when cache/biome coverage becomes too small.
- Optional ZoneManager blocked-zone integration.
- Admin spawn visualization and regeneration commands.
- Compatibility API hooks for other plugins.

## Optional integration

`ZoneManager` — used only when blocked zone IDs are configured.

## Permissions

- `roguerustrandospawns.admin` — administrative visualization/regeneration commands.

## Commands

- `/showspawns` — aliases `roguerust.showspawns`, `randospawns.show`; draws generated points for 30 seconds. Player-only, 2-second cooldown.
- `/randospawns.regenerate` — alias `roguerust.randospawns.regenerate`; regenerates the cache. Console allowed, 10-second cooldown.

## Configuration

Configuration file: `config/RogueRustRandoSpawns.json`

### Generation Options

| Setting | Default | Validation |
| --- | ---: | --- |
| Generation attempts | `4000` | minimum `100` |
| Maximum slope (degrees) | `45` | `0-89` |
| Distance from buildings (metres) | `18` | `>= 0` |
| Map radius used for spawn generation | `0.95` | `0.1-1.0` |
| Vertical spawn offset | `0.15` | — |
| Ground probe height | `8` | minimum `1` |
| Persist generated spawn cache | `true` | — |
| Minimum cached points before regeneration | `250` | minimum `1` |
| Regenerate when a biome falls below this many points | `10` | minimum `0` |
| Maximum nearby entities checked per candidate | `64` | minimum `8` |

### Spawn Options

Default biome thresholds:

| Biome | Enabled | Minimum online players |
| --- | --- | ---: |
| Arctic | `true` | `30` |
| Tundra | `true` | `20` |
| Arid | `true` | `10` |
| Temperate | `true` | `1` |
| Jungle | `true` | `1` |

Other defaults:

- Blocked zone IDs: empty.
- Blocked topologies: `Cliff`, `Cliffside`, `Lake`, `Ocean`, `Monument`, `Offshore`, `River`, `Swamp`, `Rail`.
- Maximum biome selection attempts per respawn: `8`.
- Candidate checks per biome: `12`.

## Data

Spawn cache key:

`RogueRustRandoSpawns/spawn-cache`

The cache stores world seed, world size, plugin version and per-biome vectors. A cache is rejected when it belongs to a different map seed/size. Legacy `RogueRust/RogueRustRandoSpawns/spawn-cache` is migrated.

## Localization

`lang/<language>/RogueRust/RogueRustRandoSpawns/messages.json`

## Developer API

- `GetSpawnPointAtBiome(string biomeType)` — returns a `Vector3` or `null`.
- `DisableSpawnSystem()` — temporarily lets Rust's normal respawn system handle players.
- `EnableSpawnSystem()` — re-enables RogueRust random spawns.
- `GetSpawnPoint()` — returns an unrestricted cached spawn or `null`.
- `RegenerateSpawnPoints()` — regenerates and returns total point count.

## Performance notes

Spawn generation occurs at initialization/regeneration rather than every respawn. Respawns select from cached biome lists and revalidate a limited number of candidates. Nearby entity checks are capped, depleted biomes trigger coalesced regeneration, and persistent caches avoid expensive regeneration on every restart.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustRandoSpawns.cs` in the plugin directory, optionally install ZoneManager, then review biome/topology rules. Existing cache is automatically invalidated when the map seed/size changes.
