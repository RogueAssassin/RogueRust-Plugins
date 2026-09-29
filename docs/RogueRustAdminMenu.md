# RogueRustAdminMenu

> RogueRust administration workspace with F1-style RogueUI, native Rust artwork, shared teleport/vehicle services, player administration and lightweight AdminVanishUncharted.

**Version:** `2.5.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustAdminMenu.cs) · [Back to plugin catalogue](../README.md)

## Features

- RRAM administration workspace with Dashboard, Players, Give, Vehicles, Teleport, Permissions, Groups, ConVars, Plugins, Commands and Diagnostics sections, plus Player Detail and Appearance/Window workflows.
- Online/offline recent-player tracking and searchable player administration.
- Give browser with categories, search, recent/favourites, native Rust icons and ImageLibrary fallback.
- Shared RogueRust teleport and vehicle services.
- Saved teleport locations and arrival protection.
- Lightweight AdminVanishUncharted with invisibility, optional god mode and optional noclip/fly.
- Permission/group management, server ConVar management and plugin controls.
- Configurable custom player-info commands for third-party plugins.
- Optional Discord webhook audit logging.
- Confirmation prompts for destructive player actions.

## Compatibility

Designed for Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Permissions

- `roguerustadminmenu.use` — open the admin menu.
- `roguerustadminmenu.permissions` — permission administration.
- `roguerustadminmenu.groups` — group administration.
- `roguerustadminmenu.convars` — server ConVar administration.
- `roguerustadminmenu.plugins` — plugin administration.
- `roguerustadminmenu.give` — item giving.
- `roguerustadminmenu.give.selfonly` — restrict giving to self.
- `roguerustadminmenu.players` — player administration.
- `roguerustadminmenu.players.kickban` — kick/ban actions.
- `roguerustadminmenu.players.mute` — mute actions.
- `roguerustadminmenu.players.blueprints` — blueprint actions.
- `roguerustadminmenu.players.hurt` — hurt actions.
- `roguerustadminmenu.players.heal` — heal actions.
- `roguerustadminmenu.players.kill` — kill actions.
- `roguerustadminmenu.players.strip` — strip inventory actions.
- `roguerustadminmenu.players.teleport` — teleport actions.
- `roguerustadminmenu.vanish` — AdminVanishUncharted.
- `roguerustadminmenu.vehicles` — vehicle administration/spawning.

Config-defined custom AdminMenu permissions are registered dynamically. Rust server admins can automatically receive access, and the Oxide `admin` group can optionally be granted all AdminMenu permissions.

## Commands

### `/admin`
Alias: `/radmin`

Opens the administration workspace. Player-only. Access is controlled by `roguerustadminmenu.use` unless automatic Rust-admin access applies.

### `/vanish`

Toggles the built-in AdminVanishUncharted module. Requires `roguerustadminmenu.vanish` and the vanish module to be enabled.

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

Vanish targeting hooks are subscribed only while at least one administrator is vanished. The module blocks normal NPC/Bradley/helicopter targeting and suppresses hostile/metabolism effects while active.

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
  "Version (DO NOT CHANGE)": "2.5.0"
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
