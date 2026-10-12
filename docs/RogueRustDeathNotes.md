# RogueRustDeathNotes

> RogueRust death notification engine based on the Death Notes concept, with native GUI output, per-player controls and optional notification integrations.

**Version:** `2.5.3`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustDeathNotes.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **2.5.3:** restored the default display names for killers and victims (animals, NPCs, traps, turrets, locks, vehicles) and about 85 weapon and vehicle aliases. They ship in the RogueRust DLL (4.4.62 or newer) and are written to `killer-data` for editing.
- Traps, turrets and locks are named from their entity class again (`Auto Turret`, `Shotgun Trap`) instead of a raw prefab name.
- Held weapons are resolved from the running server's item list, so a new gun or tool is named correctly without a plugin update.
- `/dn off` and team-only mode now also apply to Patrol Helicopter and Bradley tag messages.
- `RoamingNPC` entities are classified as custom NPCs.

## Features

- Rich death messages for players, NPCs, traps, turrets, animals, fire, Bradley and Patrol Helicopter events.
- Native RogueRust GUI notifications with embedded notification icon.
- Chat and console output.
- Optional `Notify` and `UINotify` output modules.
- Per-player `/dn` enable/disable and team-only controls.
- Message-radius filtering and metric/imperial distance support.
- Configurable message matching by killer type, victim type and damage type with wildcard support.
- Patrol Helicopter and Bradley tag messages.
- Persistent player preferences and translation/weapon catalogue data.

## Optional integrations

- `Notify`
- `UINotify`

Neither is required when using native GUI/chat/console output.

## Permissions

- `roguerustdeathnotes.cansee` — viewing permission when permission-gated output is enabled.
- `roguerustdeathnotes.cantsee` — prevents the player from receiving notices.
- `roguerustdeathnotes.suppress` — suppresses messages involving the player.
- `roguerustdeathnotes.seeteamonly` — restricts notices to team-related events.

> **Do not grant every permission to a group.** `cantsee`, `suppress` and `seeteamonly` switch notices *off*. A group that holds all four permissions (for example an `admin` group granted everything) sees no death notes at all, and deaths involving its members are hidden from everyone.

- Additional display permissions configured for Patrol Helicopter, Bradley or individual death messages are registered dynamically when they use the `roguerustdeathnotes.*` namespace.

## Commands

### `/dn`

Player-facing death-notification controls: `/dn [on|off|toggle|teamonly|teamoff|meters|feet|yards]`. With no argument it toggles notices. Distance units are a per-player choice. Availability is controlled by `Can Player Use /dn Command` in configuration.

## Configuration

Configuration file: `config/RogueRustDeathNotes.json`

The current grouped configuration contains:

- `Formatting Settings` — variable formats, variable colours and chat message format.
- `Output Settings` — integrated output modules plus Notify/UINotify message types.
- `Player Controls` — `/dn` availability and player preference behaviour.
- `Patrol Helicopter` — display permissions, console/chat/Notify/UINotify toggles and tag message.
- `Bradley APC` — equivalent Bradley tag/output controls.
- `General` — broadcast radius, distance units, permission requirements and general behaviour.
- `Developer` — diagnostic/developer behaviour.
- Translation/death-message definitions used by the matching engine.

Older numbered/flat configuration keys are migrated into the grouped layout. Existing values are read from either the numbered legacy key or its original name.

### Message matching

Death message definitions can match killer type, victim type and damage type. `*` acts as a wildcard. Matching progressively falls back from exact combinations to wildcard combinations, allowing specific messages to override general ones.

## Data

RogueRust data keys:

- `RogueRustDeathNotes/player-settings` — per-player enabled/team-only preferences.
- `RogueRustDeathNotes/killer-data` — persistent translation/weapon catalogue data.

Legacy `RogueRust/RogueRustDeathNotes/...` data keys are recognized for migration.

## Localization

Language files use:

`lang/<language>/RogueRust/RogueRustDeathNotes/messages.json`

## Runtime and performance notes

- Attack/tag state is stored in dictionaries/sets and cleaned as events complete.
- The notification icon is supplied from the RogueRust extension resource rather than requiring a remote image service.
- Message matching uses compiled regular expressions for rich-text cleanup and staged matching delegates.
- Per-player settings prevent unwanted output without requiring global configuration changes.

## Installation

1. Install the latest [Oxide.Ext.RogueRust release](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
2. Place `RogueRustDeathNotes.cs` in the plugin directory.
3. Optionally install Notify or UINotify.
4. Review output modules, permissions, radius and message definitions.

## Updating

Replace the `.cs` file while retaining configuration and RogueRust data. The plugin includes migration handling for older configuration keys and legacy data locations.
