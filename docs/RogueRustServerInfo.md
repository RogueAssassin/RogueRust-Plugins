# RogueRustServerInfo

> In-game server information and command reference panel. Players see the commands available to them; admins also get the admin list.

**Version:** `1.1.3`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustServerInfo.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- A config file that cannot be read is copied to `<name>.json.invalid` before the defaults are written, so a typo no longer costs every setting.

## Features

- `/info` opens a panel styled to match the RogueRust administration menu, starting on an information page you write yourself.
- A **Commands** tab lists player commands. An **Admin Commands** tab appears only for admins.
- Commands registered through RogueRust are collected automatically and written into the config, where they can be edited, re-filed or hidden.
- Each entry can name its plugin and is hidden while that plugin is not loaded, so the list always matches what is installed.
- Player commands that need a permission the player lacks are hidden.
- A game tip on first wake-up tells players to type `/info`.
- The window frame is sent once; tab and page changes resend only the part that changed, and the cursor is held separately so it does not flicker.

## Permissions

- `roguerustserverinfo.admin` — shows the Admin Commands tab. Auth level 2 (owner) also counts as admin unless that is turned off in the config.

## Commands

| Command | Description |
| --- | --- |
| `/info` | Opens the panel on the information page. Alias: `/serverinfo`. |
| `/commands` | Opens the panel on the command list. |
| `/info admin` | Opens the panel on the admin list (admins only). |

## Configuration

Configuration file: `config/RogueRustServerInfo.json`

### General Settings

| Setting | Default |
| --- | --- |
| Panel Title | `{server.name}` |
| Panel Subtitle | `SERVER INFORMATION  •  COMMANDS` |
| Panel Footer | `Type /info at any time to open this panel.` |
| Commands Per Page | `10` (4–14) |
| Treat Auth Level 2 (Owner) As Admin | `true` |
| Hide Player Commands The Player Lacks Permission For | `true` |
| Add Newly Registered RogueRust Commands To The Commands List Automatically | `true` |
| Open Panel When A Player Connects | `false` |

### Game Tip

| Setting | Default |
| --- | --- |
| Enabled | `true` |
| Message | `Type /info to open the server menu and see what {server.name} offers.` |
| Delay After Waking Up (Seconds) | `4` |
| Display Seconds | `12` |
| Show Again Every (Minutes, 0 = Once Per Connection) | `0` |

### Theme

Window, header, navigation, body, footer, surface, accent, brand, danger and text colours in Rust CUI format (`red green blue alpha`, each 0–1), plus the world dimming behind the panel. The defaults match the RogueRust administration menu.

### Information Lines

A list of up to 16 lines. Start a line with `#` for a heading. Placeholders: `{server.name}`, `{player.name}`, `{players.online}`, `{players.max}`.

### Commands

Each entry has:

| Field | Meaning |
| --- | --- |
| Command | Name without the slash. |
| Description | Shown beside the command. |
| Usage | Shown under the description. |
| Category | Shown on the right; the list is sorted by category. |
| Audience (Default or Admin) | Which tab the entry appears on. |
| Required Permission (optional) | A player without it does not see the entry. |
| Plugin (optional) | The entry is hidden while this plugin is not loaded. |
| Show In Panel | Set to `false` to hide the entry. |

The defaults cover every chat command in the RogueRust plugin collection.

### Automatic command collection

Commands registered through RogueRust carry their own name, usage, description, category and permission. At startup, and whenever a plugin loads, any that are not already listed are added to the config.

- A new command goes on the player list only when RogueRust enforces a non-admin permission for it. Otherwise it starts on the admin list, because its handler may restrict access internally. Change `Audience` to move it.
- To hide a collected command, set `Show In Panel` to `false`. Deleting the entry does not work: it is added again at the next load.
- Commands registered directly with Oxide (including RogueRustSkins and most third-party plugins) expose only a name, so add those by hand.

## Localization

Tab titles and list text are in `lang/<language>/RogueRustServerInfo.json`.

## Installation

1. Install the latest [Oxide.Ext.RogueRust release](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
2. Place `RogueRustServerInfo.cs` in the plugin directory.
3. Edit `Information Lines` in the config with your server's rules and details.

## Updating

Replace the `.cs` file and keep the config. On a plugin update, default commands the config does not have yet are appended; existing entries are not changed.
