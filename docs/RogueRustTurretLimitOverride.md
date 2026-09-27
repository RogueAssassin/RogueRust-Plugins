# RogueRustTurretLimitOverride

> Configures Rust's turret interference ConVars through a small RogueRust administration plugin.

**Version:** `2.1.1`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustTurretLimitOverride.cs) · [Back to plugin catalogue](../README.md)

## Features

- Overrides `sentry.interferenceradius` and `sentry.maxinterference`.
- Can disable the override and restore the plugin's vanilla reference values (`40` radius / `12` maximum).
- Runtime admin command for status and changes.
- Validates/clamps configuration.
- Migrates older flat and numbered configuration layouts.
- Localized command responses.

## Permissions

- `roguerustturretlimitoverride.admin` — change/view turret limit settings as a player.

Rust server admins are also accepted. Server-console execution is permitted by the command implementation.

## Commands

### `/turretlimit`
Alias: `roguerust.turretlimit`

Usage:

`/turretlimit status | enabled <true|false> | radius <0-10000> | max <1-10000>`

Changes are saved immediately and reapplied to Rust's server ConVars. Command cooldown: `0.5s`.

## Configuration

Configuration file: `config/RogueRustTurretLimitOverride.json`

| Setting | Default | Validation |
| --- | ---: | --- |
| Enable Override | `true` | boolean |
| Interference Radius (0 disables radius check) | `0` | `0-10000` |
| Maximum Active Turrets in the Interference Radius | `100` | `1-10000` |

### Default configuration

```json
{
  "Turret Settings": {
    "Enable Override": true,
    "Interference Radius (0 disables the radius check)": 0.0,
    "Maximum Active Turrets in the Interference Radius": 100
  },
  "Version (DO NOT CHANGE)": "2.1.1"
}
```

When override is disabled, the plugin applies `sentry.interferenceradius=40` and `sentry.maxinterference=12`. Existing powered turrets may require a power cycle before Rust recalculates their interference state.

## Data and localization

Persistent custom data: **none**.

Language path:

`lang/<language>/RogueRust/RogueRustTurretLimitOverride/messages.json`

Legacy language locations including the previous RogueRust/TurretLimitOverride and original plugin filenames are migrated when possible.

## Migration

- `01 - Turret Settings` is renamed to `Turret Settings`.
- Legacy flat `Enable override`, radius and maximum keys are migrated.
- The obsolete configurable admin-permission field is replaced by the canonical RogueRust permission.
- Values are validated before the migrated config is saved.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustTurretLimitOverride.cs` in the plugin directory and grant the admin permission as needed. Replace the `.cs` file for updates; supported config/language layouts migrate automatically.
