# RogueRustHackableCrateTracker

> Reports who starts hacking a locked crate, who loots it, and crates left unopened after they unlock.

**Version:** `2.2.0`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustHackableCrateTracker.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **2.2.0:** in-game reporting rewritten around three messages: hack started, crate looted, and crate still unopened a few minutes after it unlocked.
- The "is attempting to hack" message is removed. It fired on every press of the laptop, including rejected ones.
- Chat shows the map grid only. Exact coordinates stay in the console and Discord reports.
- All messages moved to the language file.
- Four config keys are no longer used and are dropped on first load: `Report Hack Attempts`, `Report Hack Completion`, `Report First Actual Looter` and `Include World Metadata`.

## Features

- **Hack started** — "Rogue started hacking a locked crate at K12. It unlocks in 15m."
- **Looted** — "Rogue has looted the locked crate at K12." Sent the first time someone opens the crate after it unlocks.
- **Left unopened** — "The locked crate at K12 unlocked 3 minutes ago and nobody has opened it." Sent only if nobody has opened it by then; the delay is configurable.
- Optional message at the moment a crate unlocks (off by default).
- In-game reports can be limited to players with a permission.
- Console reporting with exact coordinates and the hacker's Steam ID.
- Built-in Discord webhook embeds, with an optional map link template using `{grid}`, `{x}`, `{z}`, `{worldsize}` and `{seed}`.
- Works for every hackable crate: CH47 drops, oil rigs, cargo ship, and crates spawned by other plugins.

## Permissions

- `roguerusthackablecratetracker.use` — receives in-game reports when `Only Send In-Game Reports to Permitted Players` is enabled.
- `roguerusthackablecratetracker.admin` — also receives restricted reports.

Server admins always receive restricted reports.

## Commands

None. Tracking is event-driven.

## Configuration

Configuration file: `config/RogueRustHackableCrateTracker.json`

### Reporting Settings

| Setting | Default |
| --- | --- |
| Enable In-Game Reporting | `true` |
| Only Send In-Game Reports to Permitted Players | `false` |
| Enable Console Reporting | `true` |
| Report When A Player Starts Hacking A Crate | `true` |
| Report The Moment A Crate Unlocks | `false` |
| Report A Crate Still Unopened This Many Minutes After It Unlocked (0 = Off) | `3` |
| Report The Player Who Loots A Crate | `true` |

### Discord Settings

| Setting | Default |
| --- | --- |
| Enabled | `false` |
| Webhook URL | empty |
| Map URL Template (`{grid}`, `{x}`, `{z}`, `{worldsize}`, `{seed}`) | empty |

When Discord is enabled with an invalid or empty webhook URL, the plugin logs a warning and carries on.

### Location Settings

| Setting | Default |
| --- | --- |
| Include Exact XYZ Coordinates In Console And Discord Reports | `true` |

## Localization

Messages are in `lang/<language>/RogueRustHackableCrateTracker.json`.

| Key | Placeholders |
| --- | --- |
| `Chat.Prefix` | — |
| `Hack.Started` | `{0}` player, `{1}` grid, `{2}` time until unlock |
| `Hack.Unlocked` | `{0}` grid |
| `Hack.Unopened` | `{0}` grid, `{1}` minutes |
| `Hack.Looted` | `{0}` player, `{1}` grid |
| `Player.Unknown` | used when the hacker cannot be identified |

## Runtime and performance notes

- The loot and entity-removed hooks are typed, so they run only for hackable crates rather than for every container opened or entity destroyed.
- Nothing runs on a timer while no crate is being hacked.
- Discord requests go through the RogueRust HTTP service with retry and rate-limit handling.

## Installation

1. Install the latest [Oxide.Ext.RogueRust release](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
2. Place `RogueRustHackableCrateTracker.cs` in the plugin directory.
3. Review the reporting settings, and add a Discord webhook if you want one.

## Updating

Replace the `.cs` file and keep the config. Discord and permission settings carry over; the new reporting settings start at their defaults.
