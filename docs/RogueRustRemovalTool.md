# RogueRustRemovalTool

> RogueRust-native building and deployable removal tool with normal/admin/bulk modes, authorization rules, costs, refunds and integrations.

**Version:** `2.0.8`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases) 4.4.69 or newer

[Plugin source](../plugins/RogueRustRemovalTool.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **2.0.8:** Requires RogueRust 4.4.75. Permissions are granted automatically the first time the plugin loads: players get `roguerustremovaltool.normal`; the admin group gets `roguerustremovaltool.admin`, `roguerustremovaltool.all`, `roguerustremovaltool.external`, `roguerustremovaltool.structure`, `roguerustremovaltool.target`. Each is granted once, so a permission you revoke stays revoked, and a permission you had already granted to a group is left as you set it.
- **2.0.7:** Bulk removal removes from the top down, so upper floors go first and nothing collapses in a cascade. A bulk job now logs one summary line instead of one audit line per entity. Single removals are still logged as before.
- **2.0.6:** The config file is rewritten only when something in it changes, instead of on every load. When the plugin version changes, the previous file is kept as `RogueRustRemovalTool.json.v<old version>.bak` before the update is saved. Requires RogueRust 4.4.69 or later. From RogueRust 4.4.71 no `.bak` copy is made: the file is updated in place, your settings are kept, and only new settings are added.
- **2.0.5:** The startup log line and the config file now show the plugin's real version (2.0.4 still reported 2.0.3).
- **2.0.4:** No code changes were needed. Checked against the 10 November 2026 Rust build and Oxide.Rust 2.0.7900.
- **2.0.3:** structure, all and external removal now work with their own permission. They used to remove nothing, and report `0 removed`, unless the player also held `roguerustremovaltool.admin`.
- `Cooldown Seconds` now takes effect. It was in the config but never applied: it is the wait between one normal session ending and the next starting. Staff modes and the override permission skip it. Set it to `0` to keep the old behaviour.
- When a click removes nothing because you cannot afford it, or the reticle text is turned off, the reason is sent in chat.
- A config section set to `null` no longer resets the whole file.
- A config file that cannot be read is copied to `<name>.json.invalid` before the defaults are written, so a typo no longer costs every setting.
- Configuration section ordering standardized while preserving existing settings and behavior.

## Features

- Normal single-entity removal sessions with cooldown, duration and removal limits.
- Building block, deployable, door, lock, IO/industrial and optional vehicle support.
- Admin/override removal.
- Structure, all, external and target bulk modes with confirmation safeguards.
- Ownership/building-privilege and entity-health/age/storage safety rules.
- Configurable costs and refunds with per-entity overrides/exclusions.
- Storage/vending/IO content handling before removal.
- HUD, target reticle, target details and cost/refund preview.
- Optional team/friend/clan/building-owner/raid-block/combat-block/economy/reward integrations.

## Optional integrations

- Friends
- Clans
- BuildingOwners
- NoEscape
- Economics
- ServerRewards

Rust Teams support is built in and configurable.

## Permissions

- `roguerustremovaltool.normal` — Default: players (default group).
- `roguerustremovaltool.admin` — Default: admins (admin group).
- `roguerustremovaltool.all` — Default: admins (admin group).
- `roguerustremovaltool.external` — Default: admins (admin group).
- `roguerustremovaltool.structure` — Default: admins (admin group).
- `roguerustremovaltool.target` — Default: admins (admin group).
- `roguerustremovaltool.override` — Default: nobody (grant it yourself).

## Commands

Primary command defaults to `/remove` and is configurable through `General Settings -> Command`. Removal modes and session arguments are handled by the command implementation/UI and permission set.

## Configuration

Configuration file: `config/RogueRustRemovalTool.json`

### General Settings defaults

`Command=remove`, session `60s`, maximum session `300s`, maximum removals `50`, distance `3m`, removal interval `0.25s`, cooldown `60s`, reset timer after successful removal `false`.

### Normal Removal defaults

Building blocks/deployables/twig/wood/stone/metal/armored/doors/locks/IO are enabled. Vehicles are disabled. Child locks are removed with the parent. Occupied entities are protected. Successful removal logging and death/disconnect session shutdown are enabled. Container/vending/IO contents are dropped according to their respective defaults.

### Access & Safety defaults

- Require Entity Ownership: `true`
- Require Building Privilege: `true`
- Block Damaged Entities: `true`
- Minimum Health Percent: `100`
- Entity Age Limit Seconds: `0` (disabled)
- Block Non-Empty Storage: `true`

### Admin Removal defaults

Damaged entities, any supported entity type and non-empty storage are allowed for admin removal.

### Bulk Removal defaults

- Entities Per Batch: `20`
- Batch Interval Seconds: `0.05`
- Maximum Entities Per Operation: `0` (unlimited)
- Structure mode includes building deployables: `false`
- All mode includes building deployables: `true`
- External radius: `12m`
- External walls/gates: enabled
- Confirmation required for structure/all/external: `true`
- Confirmation timeout: `8s`
- Show bulk entity count in HUD: `true`

### Removal Costs

Disabled by default. Building/deployable percentages default to `0`; entity-specific percentage overrides and exclusions are supported. HUD preview defaults to enabled.

### Refunds

Enabled by default. Building blocks and deployables default to `100%` refund. Entity-specific overrides/exclusions are supported. Refund-before-destroy and HUD preview default to enabled.

### Integrations defaults

Rust Teams/Friends/Clans enabled; BuildingOwners disabled; NoEscape raid/combat blocking enabled; Economics and ServerRewards disabled.

### UI defaults

UI, authorization result, center reticle and target details are enabled.

## Data and localization

The current core removal state (sessions, confirmations, entity-age observations and UI state) is runtime-managed. Configuration is the primary persistent store. Refer to source/release notes if future revisions add persistent player state.

## Safety and performance notes

- Bulk work is explicitly batched.
- Destructive bulk modes support confirmation timeouts.
- Removal interval throttles repeated actions.
- Occupied entity, ownership, privilege, damage, age and storage rules can prevent unsafe normal removal.
- Optional NoEscape integration can block removal during raid/combat states.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustRemovalTool.cs` in the plugin directory, configure safety/cost/refund rules, install any desired optional integrations and grant only the necessary removal permissions. Retain config when updating unless release notes say otherwise.
