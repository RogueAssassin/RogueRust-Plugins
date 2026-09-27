# RogueRust Day/Night Scheduler

**Version:** `3.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

RogueRust-powered day/night duration scheduling, cycle skipping, manual time control and protected freeze controls.

## Features

- Independent real-time day and night durations
- Optional automatic day or night skipping
- Manual world-time setting using decimal hour or `HH:MM`
- Freeze and unfreeze world time
- Separate auth-level requirements for duration and time/freeze commands
- RogueRust permission override
- Sunrise/sunset hook propagation
- Legacy config and language migration
- Restores previous TOD settings when unloaded

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerustdaynightscheduler.use` — allows command use without meeting the configured player auth-level requirement.

Server console commands are permitted. Player access is accepted when either the required auth level or RogueRust permission is satisfied.

## Commands

### `tod [0-24|HH:MM|freeze|unfreeze]`
Shows current time/cycle status when used without arguments. A numeric time or `HH:MM` sets world time. `freeze` and `unfreeze` control progression.

### `tod.freeze`
Freezes world time at the current hour.

### `tod.unfreeze`
Resumes normal world-time progression.

### `daynight.daylength <1-1440>`
Sets the real-time day duration in minutes and saves the configuration.

### `daynight.nightlength <1-1440>`
Sets the real-time night duration in minutes and saves the configuration.

All commands have a 0.5-second RogueCommand cooldown.

## Configuration

### Cycle Settings

- **DayLength (Length of the day in real minutes)** — default `30`; constrained to `1–1440`.
- **NightLength (Length of the night in real minutes)** — default `30`; constrained to `1–1440`.
- **Skip night immediately when the night cycle starts.** — default `false`.
- **Skip day immediately when the day cycle starts.** — default `false`.
- **Write automatic cycle skips to the server console.** — default `true`.

### Permission Settings

- **AuthLevelCmds** — minimum auth level for day/night duration commands. Default `1`; constrained to `0–2`.
- **AuthLevelFreeze** — minimum auth level for time setting and freeze commands. Default `2`; constrained to `0–2`.

### Time Control Settings

- **Freeze world time at the configured hour after startup.** — default `false`.
- **TimeToFreeze (0-24)** — default `12.0`. Values wrap into the 24-hour clock.

### Developer Settings

- **LogLevel (Error, Warning, Info, Debug)** — default `Info`. Invalid values are normalized to `Info`.

## Default Configuration

```json
{
  "Cycle Settings": {
    "DayLength (Length of the day in real minutes)": 30,
    "NightLength (Length of the night in real minutes)": 30,
    "Skip night immediately when the night cycle starts.": false,
    "Skip day immediately when the day cycle starts.": false,
    "Write automatic cycle skips to the server console.": true
  },
  "Permission Settings": {
    "AuthLevelCmds (Minimum auth level for duration commands)": 1,
    "AuthLevelFreeze (Minimum auth level for freeze/time commands)": 2
  },
  "Time Control Settings": {
    "Freeze world time at the configured hour after startup.": false,
    "TimeToFreeze (0-24)": 12.0
  },
  "Developer Settings": {
    "LogLevel (Error, Warning, Info, Debug)": "Info"
  },
  "Version (DO NOT CHANGE)": "3.1.0"
}
```

## Stored Data

No persistent plugin data file is required by the current revision.

## Localization

Language files use `lang/<language>/RogueRust/RogueRustDayNightScheduler/messages.json`. Supported older DayNightScheduler language layouts are migrated automatically.

## Hooks

When a real day/night transition occurs, the plugin calls `OnTimeSunrise` or `OnTimeSunset` for compatibility with other server plugins.

## Runtime Behaviour

The plugin waits for Rust's TOD system to become available and retries initialization up to ten times. It records the original TOD progression/time-curve settings and restores them on unload. Day and night durations are translated into TOD's full-day length so the requested real-time segment duration remains accurate relative to current sunrise/sunset hours.

## Configuration Migration

The v3.0.0 flat configuration layout is detected and migrated into the grouped RogueRust layout automatically.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Install the latest [Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
3. Download [RogueRustDayNightScheduler.cs](../plugins/RogueRustDayNightScheduler.cs).
4. Place it in the framework plugin directory.
5. Review auth levels and grant `roguerustdaynightscheduler.use` only where needed.

## Updating

Replace the `.cs` file with the newer revision. Existing grouped or supported legacy configuration/language data is retained or migrated. Do not manually alter `Version (DO NOT CHANGE)`.

## Source

[View the plugin source](../plugins/RogueRustDayNightScheduler.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
