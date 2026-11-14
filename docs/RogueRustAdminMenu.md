# RogueRustAdminMenu

> RogueRust administration workspace with F1-style RogueUI, native Rust artwork, shared teleport/vehicle services, player administration and lightweight vanish.

**Version:** `2.5.15`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases) 4.4.69 or newer

[Plugin source](../plugins/RogueRustAdminMenu.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **2.5.15:** The audit log, command help and plugin description say "vanish" instead of the old module name "AdminVanishUncharted". Old config keys that use that name are still read when migrating.
- **2.5.14:** Requires RogueRust 4.4.75. Permissions are granted automatically the first time the plugin loads: the admin group gets all 26 admin permissions. Each is granted once, so a permission you revoke stays revoked, and a permission you had already granted to a group is left as you set it. The built-in permissions are no longer re-granted to the admin group on every load, so a permission you revoke stays revoked. Custom command permissions from the config are still granted to the admin group, once each.
- **2.5.13:** Vanish now uses Rust's own admin invisibility, the same one `debug.invis` uses. Your held item is hidden too, your voice is muted, other players pass through you, and fall effects are silenced. Turrets, traps and NPC scans can't find you, and auto turrets are also blocked from targeting you. Vanish now lasts through reconnects, plugin reloads and server restarts, and your sleeper stays hidden while you're offline. A new **VANISH ON/OFF** button on the dashboard toggles it. Vanished admins with `roguerustadminmenu.vanish.unlock` can open locks. Anti-hack violations are ignored while you're vanished. Rust's own invisible icon is shown next to the RogueRust HUD. In the item browser, toggling a favourite star now updates only that star instead of redrawing all 60 items.
- **2.5.12:** Fixes the config file growing on every load. Each load added another copy of every entry in Player Info Custom Commands and Admin Commands (a long-running server could have dozens). Existing duplicates are removed automatically on the first load, and the cleanup is logged.
- **2.5.11:** Unloading (plugin reload or server shutdown) writes only data that is still pending, instead of rewriting every data file. Saved locations and moderation notes are already written when they change. Unload took about 185 ms before.
- **2.5.10:** The item, convar and permission lists are built the first time an admin opens those screens, not during server start (removes most of the 145 ms `OnServerInitialized` warning). Permission changes from other plugins now only mark the list for a rebuild. The config upgrade is logged once, by RogueRust.
- **2.5.9:** The config file is rewritten only when something in it changes, instead of on every load. When the plugin version changes, the previous file is kept as `RogueRustAdminMenu.json.v<old version>.bak` before the update is saved. Requires RogueRust 4.4.69 or later. From RogueRust 4.4.71 no `.bak` copy is made: the file is updated in place, your settings are kept, and only new settings are added.
- **2.5.8:** The config migration log line shows the old version as a number (2.5.5) instead of a JSON object.
- **2.5.7:** The plugin's internal version now matches its published version, so the config file records the right version (the previous release still wrote the version before it).
- **2.5.6:** Compiles with no warnings: its nullable annotations now have a declared context, and the header comment shows the real version. Checked against the 10 November 2026 Rust build and Oxide.Rust 2.0.7900.
- **2.5.5:** vanish's second line of defence now works. It answered `CanNpcAttack`, which Oxide never calls; it now answers `OnNpcAttack`, so an animal or NPC that had already targeted an admin before they vanished cannot attack them.
- The Give page's vehicle artwork lookup is guarded: if the RogueRust image service fails, vehicles are shown without artwork and one warning is logged, instead of the page failing to draw.
- **2.5.4:** access is checked again on every draw and every click. If a player loses the menu permission while it is open, the menu closes; if they lose one page, they are returned to the dashboard.
- The header shows the real AdminMenu and RogueRust versions instead of two from many releases ago.
- A config file that cannot be read is copied to `<name>.json.invalid` before the defaults are written, so a typo no longer costs every setting.

## Features

- RRAM administration workspace with Dashboard, Players, Give, Vehicles, Teleport, Permissions, Groups, ConVars, Plugins, Commands and Diagnostics sections, plus Player Detail and Appearance/Window workflows.
- Online/offline recent-player tracking and searchable player administration.
- Give browser with categories, search, recent/favourites, native Rust icons and ImageLibrary fallback.
- Shared RogueRust teleport and vehicle services.
- Saved teleport locations and arrival protection.
- Lightweight vanish with invisibility, optional god mode and optional noclip/fly.
- Permission/group management, server ConVar management and plugin controls.
- Configurable custom player-info commands for third-party plugins.
- Optional Discord webhook audit logging.
- Confirmation prompts for destructive player actions.

## Compatibility

Designed for Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Permissions

- `roguerustadminmenu.use` — open the admin menu. Default: admins (admin group).
- `roguerustadminmenu.permissions` — permission administration. Default: admins (admin group).
- `roguerustadminmenu.groups` — group administration. Default: admins (admin group).
- `roguerustadminmenu.convars` — server ConVar administration. Default: admins (admin group).
- `roguerustadminmenu.plugins` — plugin administration. Default: admins (admin group).
- `roguerustadminmenu.give` — item giving. Default: admins (admin group).
- `roguerustadminmenu.give.selfonly` — restrict giving to self. Default: admins (admin group).
- `roguerustadminmenu.players` — player administration. Default: admins (admin group).
- `roguerustadminmenu.players.kickban` — kick/ban actions. Default: admins (admin group).
- `roguerustadminmenu.players.mute` — mute actions. Default: admins (admin group).
- `roguerustadminmenu.players.blueprints` — blueprint actions. Default: admins (admin group).
- `roguerustadminmenu.players.hurt` — hurt actions. Default: admins (admin group).
- `roguerustadminmenu.players.heal` — heal actions. Default: admins (admin group).
- `roguerustadminmenu.players.kill` — kill actions. Default: admins (admin group).
- `roguerustadminmenu.players.strip` — strip inventory actions. Default: admins (admin group).
- `roguerustadminmenu.players.teleport` — teleport actions. Default: admins (admin group).
- `roguerustadminmenu.players.inventory` — view and manage player inventories. Default: admins (admin group).
- `roguerustadminmenu.players.spectate` — spectate players. Default: admins (admin group).
- `roguerustadminmenu.players.notes` — moderation notes. Default: admins (admin group).
- `roguerustadminmenu.players.bulk` — bulk player actions. Default: admins (admin group).
- `roguerustadminmenu.commands` — the admin command page. Default: admins (admin group).
- `roguerustadminmenu.audit` — audit history. Default: admins (admin group).
- `roguerustadminmenu.diagnostics` — diagnostics page. Default: admins (admin group).
- `roguerustadminmenu.vanish` — vanish. Default: admins (admin group).
- `roguerustadminmenu.vanish.unlock` — open locked doors and boxes while vanished. Default: admins (admin group).
- `roguerustadminmenu.vehicles` — vehicle administration/spawning. Default: admins (admin group).

Config-defined custom AdminMenu permissions are registered dynamically. Rust server admins can automatically receive access, and the Oxide `admin` group can optionally be granted all AdminMenu permissions.

## Commands

### `/admin`
Alias: `/radmin`

Opens the administration workspace. Player-only. Access is controlled by `roguerustadminmenu.use` unless automatic Rust-admin access applies.

### `/vanish`

Toggles the built-in vanish module. Requires `roguerustadminmenu.vanish` and the vanish module to be enabled. The dashboard's **VANISH ON/OFF** button does the same.

## Configuration

Configuration file: `config/RogueRustAdminMenu.json`

### General Settings

| Setting | Default | Purpose |
| --- | --- | --- |
| Confirm Destructive Player Actions | `true` | Requires confirmation before destructive player actions. |

### Permission Settings

| Setting | Default |
| --- | --- |
| Use Different Permissions For Each Player Administration Section | `false` |
| Automatically Allow Rust Server Admins All AdminMenu Permissions | `true` |
| Automatically Grant All AdminMenu Permissions To The Oxide Admin Group | `true` |

From 2.5.14, RogueRust 4.4.75 grants the built-in AdminMenu permissions to the Oxide `admin` group, once each. The last setting now covers only the custom command permissions in your config: each is granted to the admin group once, and recorded in `data/RogueRust/RogueRustAdminMenu/granted_custom_permissions.json`. A permission you revoke is never granted again.

### Commands

`Player Info Custom Commands` accepts command groups with a required plugin, required permission, display name, command, close-on-run flag and command type (`Chat` or `Console`). Defaults include examples for Backpacks, InventoryViewer and Freeze when those plugins are installed.

### Teleport

| Setting | Default |
| --- | --- |
| Arrival Protection Seconds (0-60) | `45` |
| Remove Protection When Protected Player Attacks | `true` |

### Vanish

| Setting | Default |
| --- | --- |
| Enable Lightweight RogueRust Admin Vanish Module | `true` |
| Include God Mode While Vanished | `true` |
| Include Flying / Noclip While Vanished | `true` |
| Hide Vanished Admins From Normal Player Networking | `true` |
| Show Rust's Own Invisible Icon While Vanished | `true` |
| Stay Vanished Through Reconnects, Plugin Reloads And Restarts | `true` |
| Let Vanished Admins Open Locks (needs roguerustadminmenu.vanish.unlock) | `true` |
| Ignore Anti-Hack Violations While Vanished | `true` |

Hiding uses Rust's native admin invisibility (the same as `debug.invis`). Only spectators can see a vanished admin or their held item, their voice is muted, and their collider stays off. They are removed from AI memory and the server's player grid, so turrets, traps and NPC scans skip them. Each of these is set once when you vanish; nothing runs per frame. Targeting hooks are subscribed only while at least one administrator is vanished. They block NPC, Bradley, helicopter and auto-turret targeting, plus hostility and metabolism effects. Vanished admins are saved to `data/RogueRust/RogueRustAdminMenu/vanished_admins.json`.

### UI

| Setting | Default |
| --- | --- |
| Items / Rows Per Page | `18` |
| Use Native Rust Item Icons As Give Fallback | `true` |
| Prefer ImageLibrary Item Images In Give | `true` |
| Server Time Confirmation Duration Seconds (10-30) | `10` |
| AdminMenu UI Scale (0.85-1.15) | `1.0` |

The `UI Theme` object contains the full F1-style colour palette and can be customized.

### Integrations

`Log Menu Actions To Discord Webhook (webhook URL)` defaults to an empty string. When configured, administrative actions are posted to that webhook.

### Data

`Recent Players Purge Time (days)` defaults to `7`.

### Representative default configuration

```json
{
  "General Settings": { "Confirm Destructive Player Actions": true },
  "Permission Settings": {
    "Use Different Permissions For Each Player Administration Section": false,
    "Automatically Allow Rust Server Admins All AdminMenu Permissions": true,
    "Automatically Grant All AdminMenu Permissions To The Oxide Admin Group": true
  },
  "Teleport Settings": {
    "Arrival Protection Seconds (0-60)": 45.0,
    "Remove Protection When Protected Player Attacks": true
  },
  "Vanish Settings": {
    "Enable Lightweight RogueRust Admin Vanish Module": true,
    "Include God Mode While Vanished": true,
    "Include Flying / Noclip While Vanished": true,
    "Hide Vanished Admins From Normal Player Networking": true
  },
  "Integrations": { "Log Menu Actions To Discord Webhook (webhook URL)": "" },
  "Data Settings": { "Recent Players Purge Time (days)": 7 },
  "Version (DO NOT CHANGE)": {
    "Major": 2,
    "Minor": 5,
    "Patch": 12
  }
}
```

The generated file also contains `Command Settings` and complete `UI Settings`/theme values.

## Data and localization

RogueRust data keys:

- `RogueRustAdminMenu/recent_players`
- `RogueRustAdminMenu/saved_locations`

Language file:

`lang/<language>/RogueRust/RogueRustAdminMenu/messages.json`

Legacy RogueRust/AdminMenu language layouts are migrated when possible.

## Integrations

The menu resolves RogueRust teleport, vanish and vehicle services. Custom player-info command groups can integrate third-party plugins such as Backpacks, InventoryViewer and Freeze without making them hard dependencies. Discord audit logging is optional.

## Performance notes

- Plugin and ConVar information is cached instead of rebuilt continuously.
- Item search keys and artwork lookups are cached.
- Vanish targeting hooks are enabled only while needed.
- UI notifications replace their named popup instead of stacking.
- Immediate actions such as rapid Give remain non-blocking and avoid rebuilding the full workspace.
- Dashboard/diagnostics are demand-driven; the plugin does not add a continuous UI polling loop.
- Shared RogueRust services are reused for expensive infrastructure rather than duplicating world scans, timers or network work in the plugin.
- Vehicle and give artwork prefers native Rust data and caches fallback lookups.

## Installation

1. Install the latest [Oxide.Ext.RogueRust release](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
2. Place `RogueRustAdminMenu.cs` in the server plugin directory.
3. Allow the framework to compile/load the plugin.
4. Review `RogueRustAdminMenu.json`.
5. Grant only the permissions required by each staff role.

## Updating

Replace the `.cs` file with the newer revision. The plugin migrates older flat configuration and legacy language layouts where supported. Keep the existing config/data unless release notes explicitly require otherwise.
