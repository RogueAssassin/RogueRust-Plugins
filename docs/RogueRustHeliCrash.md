# RogueRustHeliCrash

> A malfunctioning patrol helicopter flies in and crashes. The crash site is marked on the map, guarded, and reported until it is looted or expires.

**Version:** `1.0.2`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases) 4.4.62 or newer

[Plugin source](../plugins/RogueRustHeliCrash.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **1.0.2:** guards now spawn. Placement was asking Unity's navigation mesh, which is empty now that Rust runs its AI on its own; the plugin asks the game's navigation mesh first. If the configured guard prefab does not exist, built-in alternatives are tried and the one used is logged.
- The helicopter now comes down at the chosen site. A dying patrol helicopter normally flies to the nearest monument within 1,200 m before it spins out; that rule is switched off for the moment the crash starts.
- **1.0.1:** a crash site now needs dry land for 70 m in every direction. The helicopter spins before it explodes and throws its crates outwards, so a site on the shoreline could put the loot in the sea.

## Features

- **Site selection** — a random point on land with dry ground all round, away from water, cliffs, monuments, the map edge and player buildings, or one of your own custom locations with a random offset.
- **Fly-in** — a patrol helicopter appears about 1,000 m out and flies to the site. By default it cannot be damaged and does not attack on the way.
- **Crash** — near the site it goes through the game's own shot-down sequence: spin, explosion, burning crates.
- **Crash site** — marked, guarded and reported until every crate is looted or the site expires.
- **Map** — a native explosion or crate icon, a labelled marker and a coloured circle.
- **On-screen alert** — a banner with the crash icon, the grid, and the distance and direction to the wreck. The icon ships inside the RogueRust DLL.
- **Reporting** — chat, game tips, console log, an optional Discord webhook, and `/helicrash` for players.
- **Guards** — vanilla scientists around the wreck. No other plugin is needed.
- **Loot options** — number of crates, time until they unlock, and an optional hackable locked crate.
- Hooks are subscribed only while an event is running.

## Permissions

- `roguerusthelicrash.admin` — start, place, stop and reschedule the event. Auth level 2 and the server console are also allowed.

## Commands

| Command | Who | Description |
| --- | --- | --- |
| `/helicrash` | Everyone | Shows the active crash site: grid, crates left, time remaining, and distance and direction. Alias: `/crashsite`. |
| `/helicrash start` | Admin | Starts an event at a random site. |
| `/helicrash here` | Admin | Starts an event that crashes at your position. |
| `/helicrash at <x> <z>` | Admin | Starts an event that crashes at the given coordinates. |
| `/helicrash stop` | Admin | Cancels the event and removes the helicopter, markers, guards and crates. |
| `/helicrash next [minutes]` | Admin | Shows, or sets, the time until the next automatic event. |

## Configuration

Configuration file: `config/RogueRustHeliCrash.json`

### Schedule

| Setting | Default |
| --- | --- |
| Start Events Automatically | `true` |
| Minimum Minutes Between Events | `60` |
| Maximum Minutes Between Events | `180` |
| Minimum Players Online For An Automatic Event | `4` |

### Flight

| Setting | Default |
| --- | --- |
| Distance From The Crash Site The Helicopter Appears At (Metres) | `1000` |
| Crash When Within This Distance Of The Site (Metres) | `40` |
| Crash Wherever It Is After This Many Seconds In Flight | `300` |
| Helicopter Cannot Be Damaged Before It Crashes | `true` |
| Helicopter Does Not Attack Players | `true` |

### Crash Site

| Setting | Default |
| --- | --- |
| Keep At Least This Far From Player Buildings (Metres, 0 = Off) | `60` |
| Keep At Least This Far From Monument Centres (Metres, 0 = Off) | `150` |
| Keep At Least This Far From The Map Edge (Metres) | `250` |
| Steepest Ground Allowed (Degrees) | `25` |
| Only Crash At The Custom Locations Below | `false` |
| Random Offset Applied To A Custom Location (Metres) | `25` |
| Custom Locations | empty list of `{ "X": 0, "Z": 0 }` |
| Site Stays Active For (Minutes) | `20` |

### Loot

| Setting | Default |
| --- | --- |
| Helicopter Crates Dropped (0 = Game Default) | `0` |
| Seconds Until The Crates Stop Burning And Unlock (0 = Game Default) | `0` |
| Also Drop A Hackable Locked Crate | `false` |
| Remove Unlooted Crates When The Site Expires | `false` |

### Guards

| Setting | Default |
| --- | --- |
| Enabled | `true` |
| Number Of Guards | `4` |
| Guard Prefab | `assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roamtethered.prefab` |
| Guard Health (0 = Prefab Default) | `0` |
| Spawn Within This Distance Of The Wreck (Metres) | `18` |
| Remove Surviving Guards When The Site Ends | `true` |

Guards are placed only where the game's navigation mesh reaches, so a wreck on rough or wet ground may get fewer than configured. The log says how many were placed when it is fewer.

### Map Marker

| Setting | Default |
| --- | --- |
| Show A Labelled Marker On The Map | `true` |
| Marker Label | `Helicopter Crash` |
| Native Map Icon At The Wreck (Explosion, Crate or None) | `Explosion` |
| Show A Coloured Circle On The Map | `true` |
| Circle Size (0.05 - 2.5) | `0.4` |
| Circle Opacity (0 - 1) | `0.45` |
| Circle Colour (Red Green Blue, Each 0-1) | `1 0.3 0.1` |

Rust's in-game map can only draw the marker types built into the client, so a server cannot place a custom image on it. The wreck therefore uses the closest native markers, and the custom crash icon appears in the on-screen alert.

### Reporting

| Setting | Default |
| --- | --- |
| Announce In Chat | `true` |
| Chat Prefix | `[Heli Crash]` in orange |
| Show A Game Tip When The Helicopter Appears And When It Crashes | `true` |
| Game Tip Seconds | `8` |
| Show An On-Screen Alert With The Crash Icon When It Comes Down | `true` |
| On-Screen Alert Seconds | `10` |
| Announce The First Player To Open A Crate | `true` |
| Hide The Death Notes Message For The Crash | `true` |
| Discord Webhook URL (Empty = Off) | empty |

## Data and localization

- `RogueRustHeliCrash/state` — the next scheduled event and the last crash. The schedule survives a restart, and an event never starts the moment the server boots.
- `lang/<language>/RogueRustHeliCrash.json` — every announcement and status line.

## Developer API

Hooks raised:

| Hook | Arguments |
| --- | --- |
| `OnHeliCrashStarted` | `PatrolHelicopter helicopter, Vector3 site` |
| `OnHeliCrashLanded` | `Vector3 site, string grid` |
| `OnHeliCrashLooted` | `BasePlayer player, Vector3 site` |
| `OnHeliCrashEnded` | `string outcome` — `looted`, `expired`, `stopped`, `lost` or `unloaded` |

Calls answered:

| Call | Returns |
| --- | --- |
| `IsHeliCrashHelicopter(BaseEntity)` | `true` when the entity is the event's helicopter, otherwise `null` |
| `IsHeliCrashActive()` | `bool` |
| `GetHeliCrashSite()` | `Vector3` while a site is active, otherwise `null` |

## Works with

- **RogueRustHeliLeashControl** 2.1.3+ leaves the crash helicopter alone.
- **RogueRustDeathNotes** — the "Patrol Helicopter was destroyed" note is suppressed for the crash.
- **RogueRustHackableCrateTracker** reports the optional hackable crate like any other.
- **RogueRustEventDirector** — while the crash helicopter is in the air it counts as an active patrol helicopter, which can push a scheduled patrol back by a retry interval.

## Installation

1. Install [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases) 4.4.62 or newer.
2. Place `RogueRustHeliCrash.cs` in the plugin directory.
3. Run `/helicrash here` on open ground to see a full event, then adjust the config.

## Updating

Replace the `.cs` file and keep the config and data.
