# RogueRustGridPower

> RogueRust-native GridPower controller for automatic world streetlights, deterministic density, diagnostics and player-facing grid events.

**Version:** `2.0.3`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustGridPower.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- A config file that cannot be read is copied to `<name>.json.invalid` before the defaults are written, so a typo no longer costs every setting.

## Features

- Automatic world streetlight control by Rust world hour.
- Deterministic streetlight density/selection seed.
- Optional control of Rust's native power grid: access-point output by server values or a fixed amount, optionally only while the street lights are on.
- Transformers fitted to native power poles that are missing one, a few per frame.
- Wooden ladders can be placed on power poles.
- GameTip notifications for night/dawn transitions.
- Street light cache refresh on a timer, with a per-frame budget.
- Administrative status, refresh, apply, native-grid inspection and debug markers.
- RogueRust shared GridPower backend integration.

## Permissions

- `roguerustgridpower.admin` — administrative GridPower commands and diagnostics.

## Commands

All commands require `roguerustgridpower.admin` and also answer to a `gridpower.*` alias (for example `gridpower.status`).

| Command | Description |
| --- | --- |
| `rrgrid.help` | Shows the administration and diagnostic commands. |
| `rrgrid.status` | Shows streetlight and native grid status. |
| `rrgrid.apply` | Reapplies configuration: streetlight density and streaming, the current world-time streetlight state and the native grid output policy. |
| `rrgrid.refresh` | Rebuilds the streetlight references and reapplies the current state. |
| `rrgrid.grid` | Shows the public-grid state and your grid cell. |
| `rrgrid.native` | Shows Rust native power-grid access points, wiring and the active output policy. |
| `rrgrid.native.refresh` | Re-indexes native access points and re-pushes the output policy in frame-budgeted batches. |
| `rrgrid.native.inspect` | Inspects the nearest native access point: saving flag, wired slots, current energy and maximum output. |
| `rrgrid.native.poles [apply]` | Shows native pole transformer fitting. `apply` re-runs it, filling poles that are missing one. |
| `rrgrid.debug` | Draws temporary admin-only markers for access points and streetlight fixtures within 200 m. |
| `rrgrid.target` | Inspects the world object under your cursor without modifying it. Alias: `rrgrid.cursor`. |

## Configuration

Configuration file: `config/RogueRustGridPower.json`

### General Settings

| Setting | Default |
| --- | --- |
| Enable Plugin | `true` |

### Street Lights

| Setting | Default |
| --- | --- |
| Enabled | `true` |
| Turn On Hour (0-23) | `20` |
| Turn Off Hour (0-23) | `8` |
| Street Light Density Percent (0-100) | `100` |
| Selection Seed | `0` |

The seed decides which lights are lit when the density is below 100, so the same lights are chosen after every restart.

### Native Power Grid

Controls the output of Rust's own power-grid access points. Off by default.

| Setting | Default |
| --- | --- |
| Enabled | `false` |
| Output Mode (Vanilla, Convars, Fixed) | `Convars` |
| Convars: Base Output (-1 = keep server value) | `-1` |
| Convars: Maximum Output (-1 = keep server value) | `600` |
| Override Power Plant Requirement | `false` |
| Fixed: Output | `100` |
| Only While Grid Online (follows street light hours) | `false` |
| Respect powergrid.enabled | `true` |

- **Vanilla** leaves the game's output alone.
- **Convars** sets the server's base and maximum output values.
- **Fixed** gives every access point the same output.

### Native Pole Transformers

| Setting | Default |
| --- | --- |
| Enabled | `true` |
| Density Percent (share of native poles with a transformer) | `100` |
| Selection Seed | `0` |
| Transformers Fitted Per Frame | `4` |

A pole that is missing a transformer is fitted with one; a pole never gets a second.

### Power Pole Ladders

| Setting | Default |
| --- | --- |
| Allow Wooden Ladders On Power Poles | `true` |
| Pole Detection Radius | `4` |
| Show Placement GameTip | `true` |
| Placement GameTip Duration Seconds | `2.5` |
| Placement Success Message | `Ladder attached to the power pole.` |

### Player Notifications

| Setting | Default |
| --- | --- |
| Show GameTips | `true` |
| GameTip Duration Seconds | `8` |
| Night Message | `As night settles in, the grid generators rumble to life and the roadside lights begin to glow.` |
| Dawn Message | `As daylight returns, the grid generators wind down and the streetlights fade out.` |

### Performance Settings

| Setting | Default |
| --- | --- |
| Street Light Check Interval Seconds | `30` |
| Refresh Street Light Cache Minutes | `10` |
| Street Light Frame Budget Milliseconds | `1.0` |
| Street Light Fixtures Per Frame (0 = all at once) | `12` |
| Native Grid Refresh Budget Milliseconds | `1.0` |
| Native Grid Access Points Refreshed Per Frame | `32` |

### Developer Settings

| Setting | Default |
| --- | --- |
| Log Diagnostics | `false` |

## Ladder placement notes

Players holding a wooden wall ladder can place it directly against a power pole. The plugin caches eligible pole positions for short periods, verifies the player is actually targeting a power-pole collider, spawns the native ladder entity with ownership/skin preserved, and fires the normal `OnEntityBuilt` hook for compatibility.

This feature can be disabled independently without affecting street lights or the native power grid.

## Runtime and performance notes

The plugin uses RogueRust's shared GridPower service. Heavy administrative work is queued cooperatively rather than producing large entity/network bursts in a command frame. Streetlight checks and cache refreshes run on configurable conservative intervals, and diagnostics are opt-in.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustGridPower.cs` in the plugin directory, and grant `roguerustgridpower.admin` to administrators. Existing config values are preserved while missing sections/defaults are added during version migration.
