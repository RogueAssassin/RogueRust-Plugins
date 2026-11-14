# RogueRustServerRestarter

> Schedules safe server restarts with RogueRust runtime protection, chat, game-tip and GUI warnings.

**Version:** `2.1.6`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases) 4.4.69 or newer

[Plugin source](../plugins/RogueRustServerRestarter.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **2.1.6:** The config file is updated in place: settings already in your file keep their values, and only settings new in this release are added. With RogueRust 4.4.71 or later no `.bak` copy is made, and the log lists the settings that were added. The config is read with the same settings as every other RogueRust plugin, so lists in your file are never merged with the defaults. Your restart times and warning minutes are kept in the order you wrote them; blank, invalid and repeated entries are skipped when the schedule is built instead of being removed from the file. An old flat config keeps its game tip delay as it was.
- **2.1.5:** The config file is rewritten only when something in it changes, instead of on every load. When the plugin version changes, the previous file is kept as `RogueRustServerRestarter.json.v<old version>.bak` before the update is saved. Requires RogueRust 4.4.69 or later.
- **2.1.4:** The plugin's internal version now matches its published version, so the config file records the right version (the previous release still wrote the version before it).
- **2.1.3:** No code changes were needed. Checked against the 10 November 2026 Rust build and Oxide.Rust 2.0.7900.
- A config file that cannot be read is copied to `<name>.json.invalid` before the defaults are written, so a typo no longer costs every setting.

## Features

- Multiple daily restart times using server-local `HH:mm` values.
- Configurable warning schedule and per-minute warning text.
- Chat, Rust GameTip and CUI warning output.
- Live one-second GUI countdown near restart time.
- Safe save/restart command flow.
- Test/reload/status administration surface.
- Optional automatic permission grants to the built-in `admin` group.
- Legacy flat ServerRestarter configuration migration.

## Permissions

- `roguerustserverrestarter.use`
- `roguerustserverrestarter.admin`

When enabled, both are automatically granted to the Oxide `admin` group.

## Commands

The current command family includes `serverrestart`, `serverrestart.reload` and `serverrestart.test` for status/administration and warning testing. Administrative actions require the appropriate permission.

## Configuration

Configuration file: `config/RogueRustServerRestarter.json`

### Schedule Settings

| Setting | Default |
| --- | --- |
| Scheduled Restart Times | `03:00`, `15:00` |
| Server Command to Execute After Saving | `restart 0` |

Invalid time values are ignored with a warning. Duplicate values are used once; the file is left as written. An empty valid schedule disables automatic restarts.

### Warning Settings

Warning minutes: `60, 30, 15, 5, 1` with matching default messages. Chat is enabled. GameTip delay defaults to `0`; GameTip duration defaults to `10s` and is clamped to `1-60s`.

### UI Settings

| Setting | Default | Validation |
| --- | --- | --- |
| Enable GUI Overlay | `true` | — |
| Non-Live Warning Duration | `30s` | `1-300` |
| Start Live Countdown | `5 minutes` | `0-15` |
| Panel Background | `0 0 0 0.7` | RGBA string |
| Text Color | `1 1 1 1` | RGBA string |
| Anchor Min | `0.25 0.9` | — |
| Anchor Max | `0.75 0.96` | — |
| Font Size | `20` | `8-40` |

Reason colours default to `M_DEFAULT`, `M_1` and `M_2` entries.

### Permission Settings

`Automatically Grant Use/Admin Permissions to Built-In Admin Group`: `true`

### Representative default configuration

```json
{
  "Schedule Settings": {
    "Scheduled Restart Times (HH:mm, 24-hour server-local time)": ["03:00", "15:00"],
    "Server Command to Execute After Saving": "restart 0"
  },
  "Warning Settings": {
    "Warning Times in Minutes Before Restart": [60, 30, 15, 5, 1],
    "Enable Chat Messages": true,
    "Game Tip Delay in Seconds": 0.0,
    "Game Tip Duration in Seconds": 10.0
  },
  "UI Settings": {
    "Enable GUI Overlay": true,
    "Non-Live Warning Duration in Seconds": 30.0,
    "Start Live One-Second Countdown This Many Minutes Before Restart": 5,
    "Font Size": 20
  },
  "Permission Settings": {
    "Automatically Grant Use/Admin Permissions to Built-In Admin Group": true
  },
  "Version (DO NOT CHANGE)": {
    "Major": 2,
    "Minor": 1,
    "Patch": 6
  }
}
```

## Data and localization

Persistent custom data: **none**.  
Custom language data: **none** in the current revision; warning messages are configuration-driven.

## Migration

Legacy flat configuration is converted into Schedule, Warning, UI and Permission sections. Old `restart` is normalized to `restart 0`, and invalid ranges are clamped. Duplicate and invalid schedule entries stay in the file and are skipped when the schedule is built.

## Runtime notes

The plugin schedules only the next required restart/warning work, cancels schedules cleanly on unload/reload, and destroys UI for all players during shutdown/unload.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustServerRestarter.cs` in the plugin directory, then verify restart times use the server's local clock. Replace the `.cs` file for updates; supported legacy config is migrated automatically.
