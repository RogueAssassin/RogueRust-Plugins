# RogueRustGridPower

> RogueRust-native GridPower controller for automatic world streetlights, deterministic density, diagnostics and player-facing grid events.

**Version:** `1.9.2`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustGridPower.cs) · [Back to plugin catalogue](../README.md)

## Features

- Automatic world streetlight control by Rust world hour.
- Deterministic streetlight density/selection seed.
- Optional player grid-power infrastructure and power-pole discovery.
- Configurable pole power output and transformer output limit.
- Cooperative/batched infrastructure spawning.
- Wooden ladder placement support on RogueRust-managed power poles, with cached pole targeting and native Rust ladder entities.
- GameTip notifications for night/dawn transitions.
- Streetlight/infrastructure cache refresh controls.
- Administrative status, refresh, scan, inspect, debug and power diagnostics.
- RogueRust shared GridPower backend integration.

## Permissions

- `roguerustgridpower.admin` — administrative GridPower commands and diagnostics.

## Commands

The current source exposes GridPower administrative commands for status, refresh/reapply, player-grid status, infrastructure scan, debug markers, power probing/inspection/testing/removal and climb/infrastructure inspection. These commands are intentionally diagnostic/admin-facing and use the `rrgrid` command family in the current implementation.

## Configuration

Configuration file: `config/RogueRustGridPower.json`

### General Settings

`Enable Plugin`: `true`

### Street Lights

| Setting | Default | Validation |
| --- | ---: | --- |
| Enabled | `true` | — |
| Turn On Hour (0-23) | `20` | clamped to valid world hour |
| Turn Off Hour (0-23) | `8` | clamped to valid world hour |
| Street Light Density Percent (0-100) | `100` | `0-100` |
| Selection Seed | `0` | deterministic selection seed |

### Player Grid Power

| Setting | Default |
| --- | --- |
| Enabled | `false` |
| Discover Power Poles | `true` |
| Require Vanilla Power Grid | `false` |

#### Power Poles

| Setting | Default | Validation |
| --- | ---: | --- |
| Enabled | `true` | — |
| Density Percent | `100` | `0-100` |
| Selection Seed | `0` | — |
| Power Output | `600` | `>= 0` |
| Limit Transformer Output | `true` | — |
| Transformer Maximum Output | `100` | `>= 0` |
| Spawn Batch Size | `2` | `1-2` |
| Batch Interval Seconds | `0.25` | `>= 0.25` |
| Allow Wooden Ladders On Grid Power Poles | `true` | — |
| Ladder Pole Detection Radius | `4` | clamped `1-8` metres |
| Show Ladder Placement GameTip | `true` | — |
| Ladder Placement GameTip Duration Seconds | `2.5` | clamped `1-10` |
| Ladder Placement Success Message | `Ladder attached to RogueRust GridPower pole.` | configurable text |

### Player Notifications

| Setting | Default |
| --- | --- |
| Show GameTips | `true` |
| GameTip Duration Seconds | `8` (clamped `1-30`) |
| Night Message | grid generators/streetlights night message |
| Dawn Message | daylight/grid shutdown message |

### Performance Settings

| Setting | Default | Minimum |
| --- | ---: | ---: |
| Street Light Check Interval Seconds | `30` | `5` |
| Refresh Street Light Cache Minutes | `10` | `1` |

### Developer Settings

`Log Diagnostics`: `false`. Detailed discovery/per-entity/recovery traces remain silent unless enabled.

### Representative default configuration

```json
{
  "General Settings": { "Enable Plugin": true },
  "Street Lights": {
    "Enabled": true,
    "Turn On Hour (0-23)": 20.0,
    "Turn Off Hour (0-23)": 8.0,
    "Street Light Density Percent (0-100)": 100,
    "Selection Seed": 0
  },
  "Player Grid Power": {
    "Enabled": false,
    "Discover Power Poles": true,
    "Power Poles": {
      "Enabled": true,
      "Density Percent (0-100)": 100,
      "Selection Seed": 0,
      "Power Output": 600,
      "Limit Transformer Output": true,
      "Transformer Maximum Output": 100,
      "Spawn Batch Size": 2,
      "Batch Interval Seconds": 0.25,
      "Allow Wooden Ladders On Grid Power Poles": true,
      "Ladder Pole Detection Radius": 4.0,
      "Show Ladder Placement GameTip": true,
      "Ladder Placement GameTip Duration Seconds": 2.5,
      "Ladder Placement Success Message": "Ladder attached to RogueRust GridPower pole."
    },
    "Require Vanilla Power Grid": false
  },
  "Performance Settings": {
    "Street Light Check Interval Seconds": 30.0,
    "Refresh Street Light Cache Minutes": 10.0
  },
  "Developer Settings": { "Log Diagnostics": false },
  "Version (DO NOT CHANGE)": "1.9.2"
}
```

## Ladder placement notes

When player-grid power is enabled and managed power poles are active, players holding a wooden wall ladder can place it directly against recognized RogueRust GridPower poles. The plugin caches eligible pole positions for short periods, verifies the player is actually targeting a power-pole collider, spawns the native ladder entity with ownership/skin preserved, and fires the normal `OnEntityBuilt` hook for compatibility.

This feature can be disabled independently without affecting streetlights or player-grid power.

## Runtime and performance notes

The plugin uses RogueRust's shared GridPower service. Heavy administrative work is queued cooperatively rather than producing large entity/network bursts in a command frame. Streetlight checks and cache refreshes run on configurable conservative intervals, and diagnostics are opt-in.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustGridPower.cs` in the plugin directory, and grant `roguerustgridpower.admin` to administrators. Existing config values are preserved while missing sections/defaults are added during version migration.
