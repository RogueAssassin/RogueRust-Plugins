# RogueRust Plugins

A maintained collection of **Rust server plugins** from RogueRust, designed around **Oxide and Carbon compatibility** with shared RogueRust services where applicable.

The repository is intentionally framework-neutral: documentation calls out plugin behavior and requirements rather than treating one supported mod framework as secondary.

## Plugin Catalogue

| Plugin | Version | Description | Download / Source |
|---|---:|---|---|
| **[RogueRustAdminMenu](docs/RogueRustAdminMenu.md)** | `2.1.1` | RogueRust administration workspace with F1-style RogueUI, shared teleport/vehicle services and lightweight admin vanish. | [Source](plugins/RogueRustAdminMenu.cs) |
| **[RogueRustBlueprintManager](docs/RogueRustBlueprintManager.md)** | `2.1.0` | RogueRust-powered blueprint management with legacy permission and command compatibility. | [Source](plugins/RogueRustBlueprintManager.cs) |
| **[RogueRustCraftingController](docs/RogueRustCraftingController.md)** | `2.1.0` | RogueRust-powered crafting control with legacy configuration and permission compatibility. | [Source](plugins/RogueRustCraftingController.cs) |
| **[CustomPlayerMessages](docs/RogueRustCustomPlayerMessages.md)** | `3.1.0` | Custom connect/disconnect messages and public player-list commands powered by RogueRust. | [Source](plugins/RogueRustCustomPlayerMessages.cs) |
| **[DayNightScheduler](docs/RogueRustDayNightScheduler.md)** | `3.1.0` | RogueRust-powered day/night duration scheduling, cycle skipping, and protected time controls. | [Source](plugins/RogueRustDayNightScheduler.cs) |
| **[RogueRustDeathNotes](docs/RogueRustDeathNotes.md)** | `2.2.0` | RogueRust death notification engine with integrated native GUI output and per-player controls. | [Source](plugins/RogueRustDeathNotes.cs) |
| **[RogueRustEventDirector](docs/RogueRustEventDirector.md)** | `1.1.0` | Performance-focused world event scheduling and CH47 crate direction powered by RogueRust. | [Source](plugins/RogueRustEventDirector.cs) |
| **[RogueRustFuelStackOptimizer](docs/RogueRustFuelStackOptimizer.md)** | `2.1.0` | Optimises dedicated Rust fuel containers through cached discovery using native Oxide/Carbon hooks. | [Source](plugins/RogueRustFuelStackOptimizer.cs) |
| **[RogueRustFurnaceSplitter](docs/RogueRustFurnaceSplitter.md)** | `2.1.0` | Furnace splitting with automatic fuel management, configurable stack distribution, ETA display and responsive UI. | [Source](plugins/RogueRustFurnaceSplitter.cs) |
| **[RogueRustGridPower](docs/RogueRustGridPower.md)** | `1.8.0` | Automatic world streetlights, deterministic density, diagnostics and player-facing grid events. | [Source](plugins/RogueRustGridPower.cs) |
| **[RogueRustHackableCrateTracker](docs/RogueRustHackableCrateTracker.md)** | `2.1.0` | Tracks hackable crates, hackers, looters, map grids, world metadata and Discord notifications. | [Source](plugins/RogueRustHackableCrateTracker.cs) |
| **[RogueRustHeliLeashControl](docs/RogueRustHeliLeashControl.md)** | `2.1.0` | Keeps a heavily damaged Patrol Helicopter near the last valid attacker. | [Source](plugins/RogueRustHeliLeashControl.cs) |
| **[RogueRustRandoSpawns](docs/RogueRustRandoSpawns.md)** | `2.1.0` | Random respawns with biome weighting, topology/zone protection, cached spawn generation and compatibility hooks. | [Source](plugins/RogueRustRandoSpawns.cs) |
| **[RogueRustRemovalTool](docs/RogueRustRemovalTool.md)** | `2.0.0` | RogueRust-native building and deployable removal tool. | [Source](plugins/RogueRustRemovalTool.cs) |
| **[RogueRustServerRestarter](docs/RogueRustServerRestarter.md)** | `2.1.0` | Safe scheduled restarts with runtime protection, chat, game-tip and GUI warnings. | [Source](plugins/RogueRustServerRestarter.cs) |
| **[RogueRustSkins](docs/RogueRustSkins.md)** | `2.1.1` | Performance-first Skinner-style native Rust skin browser powered by RogueRust services. | [Source](plugins/RogueRustSkins.cs) |
| **[RogueRustTurretLimitOverride](docs/RogueRustTurretLimitOverride.md)** | `2.1.1` | Configures Rust turret interference ConVars. | [Source](plugins/RogueRustTurretLimitOverride.cs) |

## Documentation

Each plugin has its own documentation page following a uMod-inspired reference structure: overview, features, requirements, permissions, commands, configuration, installation, updating and support. Documentation lives beside the plugin source so it can evolve with each revision.

## Installation

1. Run a supported Rust server with **Oxide** or **Carbon**.
2. Install the RogueRust extension/services required by the plugins you intend to use.
3. Download the desired `.cs` file from [`plugins/`](plugins/).
4. Place it in your framework's plugins directory.
5. Review the plugin's linked documentation page for permissions and configuration.

## Compatibility

The RogueRust plugin family targets Rust with Oxide/Carbon-neutral support. Individual plugin pages identify commands, permissions, configuration and requirements from the current source revision.

## Repository Layout

- `plugins/` — current plugin source files.
- `docs/` — individual plugin documentation pages.
- `README.md` — plugin catalogue and project overview.

## Support & Contributions

Use GitHub Issues for reproducible bugs or documentation corrections. Include the plugin version, framework, Rust server version, relevant logs/errors and configuration details.

## License

No blanket license is declared by this repository at this time. Unless a plugin source file states otherwise, all rights remain with its respective author.
