# RogueRust Plugins

A maintained collection of **Rust server plugins** from RogueRust, designed around **Oxide and Carbon compatibility** with shared RogueRust services where applicable.

The repository is framework-neutral: documentation describes plugin behaviour and requirements without treating either supported mod framework as secondary.

## Plugin Catalogue

| Plugin | Version | Description | Source |
|---|---:|---|---|
| **[RogueRustAdminMenu](docs/RogueRustAdminMenu.md)** | `2.1.1` | RogueRust administration workspace with F1-style RogueUI and shared administration services. | [Source](plugins/RogueRustAdminMenu.cs) |
| **[RogueRustBlueprintManager](docs/RogueRustBlueprintManager.md)** | `2.1.0` | Blueprint management with legacy permission/config compatibility. | [Source](plugins/RogueRustBlueprintManager.cs) |
| **[RogueRustCraftingController](docs/RogueRustCraftingController.md)** | `2.1.0` | Crafting control with RogueRust integration. | [Source](plugins/RogueRustCraftingController.cs) |
| **[CustomPlayerMessages](docs/RogueRustCustomPlayerMessages.md)** | `3.1.0` | Custom connect/disconnect messages and player-list commands. | [Source](plugins/RogueRustCustomPlayerMessages.cs) |
| **[DayNightScheduler](docs/RogueRustDayNightScheduler.md)** | `3.1.0` | Day/night duration scheduling and protected time controls. | [Source](plugins/RogueRustDayNightScheduler.cs) |
| **[RogueRustDeathNotes](docs/RogueRustDeathNotes.md)** | `2.2.0` | Death notification engine with native GUI output and player controls. | [Source](plugins/RogueRustDeathNotes.cs) |
| **[RogueRustEventDirector](docs/RogueRustEventDirector.md)** | `1.1.0` | Performance-focused world event scheduling and CH47 crate direction. | [Source](plugins/RogueRustEventDirector.cs) |
| **[RogueRustFuelStackOptimizer](docs/RogueRustFuelStackOptimizer.md)** | `2.1.0` | Optimises dedicated Rust fuel containers through cached discovery. | [Source](plugins/RogueRustFuelStackOptimizer.cs) |
| **[RogueRustFurnaceSplitter](docs/RogueRustFurnaceSplitter.md)** | `2.1.0` | Furnace splitting, fuel management, ETA and responsive UI. | [Source](plugins/RogueRustFurnaceSplitter.cs) |
| **[RogueRustGridPower](docs/RogueRustGridPower.md)** | `1.8.0` | Automatic world streetlights, deterministic density and diagnostics. | [Source](plugins/RogueRustGridPower.cs) |
| **[RogueRustHackableCrateTracker](docs/RogueRustHackableCrateTracker.md)** | `2.1.0` | Tracks hackable crates, hackers, looters, grids and notifications. | [Source](plugins/RogueRustHackableCrateTracker.cs) |
| **[RogueRustHeliLeashControl](docs/RogueRustHeliLeashControl.md)** | `2.1.0` | Keeps damaged Patrol Helicopters near the last valid attacker. | [Source](plugins/RogueRustHeliLeashControl.cs) |
| **[RogueRustRandoSpawns](docs/RogueRustRandoSpawns.md)** | `2.1.0` | Random respawns with biome weighting and topology/zone protection. | [Source](plugins/RogueRustRandoSpawns.cs) |
| **[RogueRustRemovalTool](docs/RogueRustRemovalTool.md)** | `2.0.0` | RogueRust-native building and deployable removal tool. | [Source](plugins/RogueRustRemovalTool.cs) |
| **[RogueRustServerRestarter](docs/RogueRustServerRestarter.md)** | `2.1.0` | Safe scheduled restarts with chat/game-tip/GUI warnings. | [Source](plugins/RogueRustServerRestarter.cs) |
| **[RogueRustSkins](docs/RogueRustSkins.md)** | `2.1.1` | Performance-first native Rust skin browser. | [Source](plugins/RogueRustSkins.cs) |
| **[RogueRustTurretLimitOverride](docs/RogueRustTurretLimitOverride.md)** | `2.1.1` | Configures Rust turret interference ConVars. | [Source](plugins/RogueRustTurretLimitOverride.cs) |

## Documentation

Every plugin has an individual documentation page. The release documentation standard includes source-verified features, permissions, commands, configuration defaults/examples, persistent data/localization, integrations/API where applicable, performance notes, installation and updating.

**[Release-readiness checklist](docs/RELEASE_READINESS.md)**

## RogueRust Extension

These plugins are built around the RogueRust extension and shared services. Download the latest DLL release here:

**[Oxide.Ext.RogueRust releases](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Installation

1. Run a supported Rust server with **Oxide** or **Carbon**.
2. Download and install the latest RogueRust DLL from **[Oxide.Ext.RogueRust releases](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the desired `.cs` from [`plugins/`](plugins/).
4. Place it in the framework plugin directory.
5. Review its documentation/configuration and grant only required permissions.

## Repository Layout

- `plugins/` — current plugin source files.
- `docs/` — individual plugin documentation and release-readiness guidance.
- `README.md` — plugin catalogue and project overview.

## Support

Use GitHub Issues for reproducible bugs or documentation corrections. Include plugin version, framework, Rust server version, relevant logs/errors and configuration details.

## License

No blanket license is declared by this repository at this time. Unless a plugin source file states otherwise, all rights remain with its respective author.
