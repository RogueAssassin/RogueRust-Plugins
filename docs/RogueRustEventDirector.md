# RogueRust Event Director

**Version:** `1.4.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Performance-focused world-event scheduling and CH47 crate direction powered by RogueRust.

## Features

- Managed scheduling for Cargo Plane, Patrol Helicopter, Bradley APC, CH47 Chinook and Cargo Ship
- Shorter randomized startup windows with configurable per-event staggering so events begin naturally after boot without clustering
- Independent enable/disable and vanilla-spawn suppression per event
- Randomized minimum/maximum intervals and spawn counts
- Per-event concurrency limits, global spacing between managed events, and dedicated retry handling for spawn failures
- Persistent next-run schedule state
- CH47 crate drop director with retry timing, crate spacing, dry-land checks, optional monument avoidance, maximum-attempt handling and configurable fallback drops
- Admin status, spawn and reschedule commands
- Randomized player GameTip announcements for Cargo Plane, Patrol Helicopter, CH47 and Cargo Ship spawns
- Persistent last-spawn timestamps alongside next-run schedules
- RogueRust world-event tracking integration
- Managed airdrops directed to dry land, with slope and player-building rules
- Map markers for the Bradley and for managed airdrops

## Recent Changes

- **1.4.0:** CH47 crates are kept off the water. `Avoid Water` used to test the helicopter's own position, which is always far above the sea, so crates could be dropped over open ocean. It now tests the ground underneath.
- The fallback drop after the maximum attempts still relaxes the monument, spacing, slope and building rules, but it no longer drops over water.
- Managed airdrops are given a checked drop point on dry land before the plane takes off.
- New **Placement Rules**: steepest ground, distance from player buildings, and distance from the map edge.
- New **Map Markers**: the Bradley and managed airdrops get a labelled marker and coloured circle, because the game has no marker of its own for either.

## Disabling vanilla spawns

Set **Disable Vanilla Spawns** to `true` on an event to stop the game spawning it on its own schedule, leaving only this plugin's schedule.

- Cargo Plane, Patrol Helicopter, CH47 and Cargo Ship are blocked at the game's event trigger, matched by the exact prefab each vanilla event spawns.
- The Launch Site Bradley is not a triggered event. Its spawner is held so it cannot re-arm itself; a Bradley the game spawned at boot before the plugin was ready is left alone.
- The startup log lists which vanilla spawns are disabled, and `rred.status` shows the same list with a count of blocked vanilla events.
- **Requires RogueRust 4.4.62 or newer.** On earlier DLL versions the setting written under its documented name was discarded when the config was loaded, so vanilla spawns were never disabled.

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerusteventdirector.admin` — required for player use of Event Director administration commands. Server console is permitted.

## Commands

### `rred.status`
Shows each managed event (enabled state, active count and time to the next run), the hackable-crate count, which vanilla spawns are disabled, and how many vanilla events have been blocked since the plugin loaded.

### `rred.next <plane|patrol|bradley|chinook|ship>`
Shows when the selected event is next scheduled.

### `rred.spawn <plane|patrol|bradley|chinook|ship>`
Immediately spawns the selected managed event. Aliases accepted by the command include `heli`, `tank`, `ch47`, `cargo`.

### `rred.trigger <plane|patrol|bradley|chinook|ship>`
Immediately triggers the selected managed event.

### `rred.delay <plane|patrol|bradley|chinook|ship> <minutes>`
Pushes the next run of the selected event back by a number of minutes.

### `rred.reschedule [plane|patrol|bradley|chinook|ship|all]`
Generates a fresh next-run time for one managed event, or for every enabled event, and saves it immediately.
|patrol|bradley|chinook|ship>`
Immediately requests the selected managed event. Aliases accepted by the command include `heli`, `tank`, `ch47`, `cargo`.

## Configuration

### Scheduler Settings

- **Tick Seconds** — scheduler check interval. Default `1`; minimum `1`.
- **Concurrency Retry Seconds** — delay before retrying an event blocked by its concurrency limit. Default `120`; minimum `30`.
- **Spawn Failure Retry Seconds** — retry delay after a managed spawn attempt fails. Default `120`; minimum `30`.
- **Minimum Seconds Between Managed Events** — global spacing between successful managed spawns. Default `600`.
- **Initial Minimum Spawn Delay Seconds** — lower bound for the first/overdue schedule after startup. Default `600`.
- **Initial Maximum Spawn Delay Seconds** — upper bound for the first/overdue schedule after startup. Default `1800`.
- **Initial Event Stagger Seconds** — additional per-event startup staggering so event schedules do not all land together.

### Event Settings

Cargo Plane, Patrol Helicopter, Bradley APC and Cargo Ship use the same base settings. CH47 adds its Drop Director section.

- **Enabled** — default `true`.
- **Disable Vanilla Spawns** — default `false`.
- **Minimum Interval Seconds** — default `3600`; minimum `30`.
- **Maximum Interval Seconds** — default `7200`; normalized to at least the minimum interval.
- **Minimum Spawn Count** — default `1`; minimum `1`.
- **Maximum Spawn Count** — default `1`; normalized to at least the minimum spawn count.
- **Maximum Concurrent** — default `1`; minimum `1`.
- **Initial Minimum Delay Seconds (0 = Scheduler Default)** — optional per-event override for the startup minimum.
- **Initial Maximum Delay Seconds (0 = Scheduler Default)** — optional per-event override for the startup maximum.

### CH47 Drop Director

- **Enabled** — default `true`.
- **Block Vanilla Drops** — default `true`.
- **Initial Delay Seconds** — default `200`.
- **Minimum Retry Seconds** — default `40`.
- **Maximum Retry Seconds** — default `60`.
- **Maximum Attempts** — maximum number of directed drop placement attempts before fallback/abort handling. Default `30`.
- **Fallback Drop After Maximum Attempts** — when enabled, drops after exhausting placement attempts, ignoring the monument, spacing, slope and building rules but still requiring dry land. If the helicopter never crosses land it leaves with its crate.
- **Minimum Crate Spacing** — default `300` metres.
- **Avoid Water** — default `true`. Tests the ground under the helicopter. This rule is never relaxed by the fallback drop.
- **Water Clearance** — default `0.25`.
- **Avoid Monuments** — default `false`.
- **Monument Radius** — default `140` metres.
- **Monuments** — optional per-monument enable/disable map.

### Placement Rules

Apply to managed airdrops and to CH47 crates.

- **Direct Managed Airdrops To Dry Land** — default `true`. The plugin picks a drop point above the water line that is not sea, lake or river bed, and hands it to the cargo plane. Vanilla airdrops and supply signals are not changed.
- **Steepest Ground Allowed For Airdrops And CH47 Crates (Degrees, 0 = Any)** — default `30`.
- **Keep Airdrops And CH47 Crates This Far From Player Buildings (Metres, 0 = Off)** — default `40`.
- **Keep Airdrops This Far From The Map Edge (Metres)** — default `250`.

### Map Markers

Cargo ships, patrol helicopters, CH47s and hackable crates create their own map marker when they spawn, including the ones this plugin spawns. The Bradley and airdrops have none in the game, so the plugin adds one.

`Bradley APC` and `Managed Airdrop` each have:

- **Enabled** — default `true`.
- **Label** — shown when the marker is hovered. Defaults `Bradley APC` and `Airdrop`.
- **Circle Size (0 = No Circle, Up To 2.5)** — default `0.3`.
- **Circle Opacity (0 - 1)** — default `0.4`.
- **Circle Colour (Red Green Blue, Each 0-1)** — red for the Bradley, blue for airdrops.

The Bradley marker follows every Bradley on the map, including ones spawned by other plugins, and is removed when it is destroyed. The airdrop marker is added to drops from this plugin's own cargo planes and is removed when the drop is looted.

### GameTip Announcements

- **Enabled** — master switch for managed-event GameTips.
- **Display Seconds** — how long the announcement remains visible.
- **Prefix** — optional text prepended to each selected message.
- Cargo Plane, Patrol Helicopter, CH47 Chinook and Cargo Ship each have independent **Enabled** switches and randomized **Messages** lists.
- The director avoids immediately repeating the same message for the same event when multiple messages are available.
- Bradley intentionally does not receive randomized GameTips; its Launch Site behavior remains controlled separately.

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
  "Version (DO NOT CHANGE)": "1.4.0"
}
```

## Configuration file

`config/RogueRustEventDirector.json`. With RogueRust 4.4.62 or newer the keys are written exactly as shown in this page. A config saved by an earlier DLL used internal names (`DisableVanillaSpawns`); it still loads and is rewritten with these names on the next save.

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
