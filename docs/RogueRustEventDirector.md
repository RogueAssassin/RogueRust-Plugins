# RogueRust Event Director

**Version:** `1.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Performance-focused world-event scheduling and CH47 crate direction powered by RogueRust.

## Features

- Managed scheduling for Cargo Plane, Patrol Helicopter, Bradley APC, CH47 Chinook and Cargo Ship
- Independent enable/disable and vanilla-spawn suppression per event
- Randomized minimum/maximum intervals and spawn counts
- Per-event concurrency limits with retry delay
- Persistent next-run schedule state
- CH47 crate drop director with retry timing, crate spacing, water checks and optional monument avoidance
- Admin status, spawn and reschedule commands
- RogueRust world-event tracking integration

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerusteventdirector.admin` — required for player use of Event Director administration commands. Server console is permitted.

## Commands

### `rred.status`
Shows RogueRust world-event tracking totals for planes, patrol helicopters, Bradley, CH47, cargo ships and hackable crates.

### `rred.spawn <plane|patrol|bradley|chinook|ship>`
Immediately requests the selected managed event. Aliases accepted by the command include `heli`, `tank`, `ch47`, `cargo`.

### `rred.reschedule`
Generates fresh next-run times for all managed event schedules and saves them immediately.

## Configuration

### Scheduler Settings

- **Tick Seconds** — scheduler check interval. Default `1`; minimum `1`.
- **Concurrency Retry Seconds** — delay before retrying an event blocked by its concurrency limit. Default `120`; minimum `30`.

### Event Settings

Cargo Plane, Patrol Helicopter, Bradley APC and Cargo Ship use the same base settings. CH47 adds its Drop Director section.

- **Enabled** — default `true`.
- **Disable Vanilla Spawns** — default `false`.
- **Minimum Interval Seconds** — default `3600`; minimum `30`.
- **Maximum Interval Seconds** — default `7200`; normalized to at least the minimum interval.
- **Minimum Spawn Count** — default `1`; minimum `1`.
- **Maximum Spawn Count** — default `1`; normalized to at least the minimum spawn count.
- **Maximum Concurrent** — default `1`; minimum `1`.

### CH47 Drop Director

- **Enabled** — default `true`.
- **Block Vanilla Drops** — default `true`.
- **Initial Delay Seconds** — default `200`.
- **Minimum Retry Seconds** — default `40`.
- **Maximum Retry Seconds** — default `60`.
- **Minimum Crate Spacing** — default `300` metres.
- **Avoid Water** — default `true`.
- **Water Clearance** — default `0.25`.
- **Avoid Monuments** — default `false`.
- **Monument Radius** — default `140` metres.
- **Monuments** — optional per-monument enable/disable map.

## Default Configuration

```json
{
  "Scheduler Settings": {
    "Tick Seconds": 1,
    "Concurrency Retry Seconds": 120
  },
  "Cargo Plane": {
    "Enabled": true,
    "Disable Vanilla Spawns": false,
    "Minimum Interval Seconds": 3600,
    "Maximum Interval Seconds": 7200,
    "Minimum Spawn Count": 1,
    "Maximum Spawn Count": 1,
    "Maximum Concurrent": 1
  },
  "Patrol Helicopter": {
    "Enabled": true,
    "Disable Vanilla Spawns": false,
    "Minimum Interval Seconds": 3600,
    "Maximum Interval Seconds": 7200,
    "Minimum Spawn Count": 1,
    "Maximum Spawn Count": 1,
    "Maximum Concurrent": 1
  },
  "Bradley APC": {
    "Enabled": true,
    "Disable Vanilla Spawns": false,
    "Minimum Interval Seconds": 3600,
    "Maximum Interval Seconds": 7200,
    "Minimum Spawn Count": 1,
    "Maximum Spawn Count": 1,
    "Maximum Concurrent": 1
  },
  "CH47 Chinook": {
    "Enabled": true,
    "Disable Vanilla Spawns": false,
    "Minimum Interval Seconds": 3600,
    "Maximum Interval Seconds": 7200,
    "Minimum Spawn Count": 1,
    "Maximum Spawn Count": 1,
    "Maximum Concurrent": 1,
    "Drop Director": {
      "Enabled": true,
      "Block Vanilla Drops": true,
      "Initial Delay Seconds": 200.0,
      "Minimum Retry Seconds": 40.0,
      "Maximum Retry Seconds": 60.0,
      "Minimum Crate Spacing": 300.0,
      "Avoid Water": true,
      "Water Clearance": 0.25,
      "Avoid Monuments": false,
      "Monument Radius": 140.0,
      "Monuments": {}
    }
  },
  "Cargo Ship": {
    "Enabled": true,
    "Disable Vanilla Spawns": false,
    "Minimum Interval Seconds": 3600,
    "Maximum Interval Seconds": 7200,
    "Minimum Spawn Count": 1,
    "Maximum Spawn Count": 1,
    "Maximum Concurrent": 1
  },
  "Version (DO NOT CHANGE)": "1.1.0"
}
```

## Stored Data

Next-run timestamps are persisted at `RogueRust/RogueRustEventDirector/schedule.json`. The older RogueRust schedule key is migrated when the current schedule is empty.

## Performance Notes

Scheduling runs through a single configurable periodic workload and uses RogueRust's tracked world-event snapshot/concurrency services rather than repeatedly searching the entire world. Schedule writes are debounced during normal operation and saved immediately for explicit reschedules/unload.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Install the latest [Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
3. Download [RogueRustEventDirector.cs](../plugins/RogueRustEventDirector.cs).
4. Place it in the framework plugin directory.
5. Review each event before enabling vanilla-spawn suppression.
6. Grant `roguerusteventdirector.admin` only to trusted administrators.

## Updating

Replace the `.cs` file with the newer revision while retaining the config and schedule data. Do not manually alter `Version (DO NOT CHANGE)`.

## Source

[View the plugin source](../plugins/RogueRustEventDirector.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
