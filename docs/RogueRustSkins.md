# RogueRustSkins

> Performance-first Skinner-style native Rust skin browser powered by RogueRust services.

**Version:** `2.3.3`  
**Author:** RogueRust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustSkins.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **2.3.3:** if a weapon's attachments do not all fit when its skin changes the item type, the ones already moved are handed back instead of being destroyed.
- Player skin sets are written on a worker thread, so the save no longer pauses the server.
- A damaged request record can no longer break `/skinrequest` or `/skinrequests`.
- A config file that cannot be read is copied to `<name>.json.invalid` before the defaults are written, so a typo no longer costs every setting.
- **2.3.2:** when RogueRustBuildingManager is installed, the Structures action and `/skinbase` hand structure skinning to it, using the skins chosen here. It checks skin ownership, spreads the work over server ticks and can undo it. Without it, structure skinning works as before.
- Configuration sections now use explicit RogueRust family ordering for stable config layout.

## Features

- Native inventory-style skin browser with 48 skins per page.
- Live Rust/Steam skin catalogue plus persistent SQLite cache.
- Item, deployable/container, crafting, inventory, team, base and bulk skin workflows.
- Automatic inventory and crafted-item skinning.
- Spray-can browser override.
- Multiple named player skin sets.
- Recent skins and favourites.
- Workshop skin requests, direct imports and collection imports.
- Paid/DLC/owned/redirected skin access controls.
- Per-feature cooldowns and batched bulk processing.
- RogueRust skin catalogue/session/player-skin service integration.

## Permissions

- `roguerustskins.use` — base `/skin` access.
- `roguerustskins.items` — deployable/item targeting.
- `roguerustskins.craft` — craft-skin controls.
- `roguerustskins.inventory` — inventory skinning.
- `roguerustskins.container` — container skinning.
- `roguerustskins.base` — base-wide operations.
- `roguerustskins.all` — broad/bulk skin operations.
- `roguerustskins.auto` — automatic skinning.
- `roguerustskins.team` — team skin features.
- `roguerustskins.request` — workshop requests.
- `roguerustskins.import` — direct Workshop import.
- `roguerustskins.admin` — administrative operations.
- `roguerustskins.bypassauth` — bypass building-authorization requirement.
- `roguerustskins.collection` — Workshop collection import.

## Commands

### Main chat commands

- `/skin`, `/s` — skin the held item/open browser.
- `/skinitem`, `/si` — skin the deployable being looked at.
- `/skincraft`, `/sc` — crafted-item skin preference.
- `/skininv`, `/sinv` — inventory skin operation.
- `/skincon`, `/scon` — container skin operation.
- `/skinset`, `/ss` — skin-set controls.
- `/skinauto` — automatic skinning.
- `/skinteam` — team workflow.
- `/skinbase` — base workflow.
- `/skinall` — bulk/all workflow.
- `/skinsearch`, `/skinrecent`, `/skinfav` — browser filters/history/favourites.
- `/skinsetname`, `/skinsets` — manage named skin sets.
- `/skinrequest`, `/skinrequests` — Workshop request workflow.
- `/skinimport`, `/skinremove` — imported Workshop skin administration.
- `/skincollection` — Workshop collection import.

### UI console commands

`rrs.page`, `rrs.search`, `rrs.view`, `rrs.set`, `rrs.option` are internal CUI/browser controls.

## Configuration

Configuration file: `config/RogueRustSkins.json`

### General Settings

| Setting | Default |
| --- | --- |
| Apply workshop skin names to items | `false` |
| Require building authorization for deployable/container skinning | `true` |
| Blacklisted skin IDs | empty |
| Blacklisted item IDs | empty |

### Skin Access Settings

| Setting | Default |
| --- | --- |
| Allow approved paid/DLC skins the player does not own | `true` |
| Allow skins the player owns | `true` |
| Include redirected/DLC item variants | `true` |

The first setting decides whether players can use paid skins they have not bought. Turn it off to limit players to skins they own.

### Automation Settings

| Setting | Default |
| --- | --- |
| Enable crafted item skinning | `true` |
| Enable automatic inventory skinning | `true` |
| Enable spray can skin browser override | `true` |
| Spray can range | `5` |
| Spray can input cooldown seconds | `0.5` |

### Skin Set Settings

| Setting | Default |
| --- | --- |
| Number of player skin sets (3-10) | `3` |

### UI Settings

| Setting | Default |
| --- | --- |
| Page UI Anchor Min | `0.5 0.0` |
| Page UI Anchor Max | `0.5 0.0` |
| Page UI Offset Min | `198 60` |
| Page UI Offset Max | `400 97` |
| Browser Header Anchor Min | `0.650 0.850` |
| Browser Header Anchor Max | `0.948 0.920` |
| Options Panel Anchor Min | `0.650 0.680` |
| Options Panel Anchor Max | `0.948 0.847` |

### Cooldown Settings

| Setting | Default |
| --- | --- |
| Enabled | `true` |
| Skin seconds | `1` |
| Skin item seconds | `2` |
| Inventory seconds | `15` |
| Container seconds | `15` |
| Team seconds | `30` |
| Base seconds | `45` |
| All seconds | `60` |

### Player History Settings

| Setting | Default |
| --- | --- |
| Recent skins per item | `6` |
| Enable favourites | `true` |
| Remove player data after inactivity days (0 disables) | `30` |

### Workshop Request Settings

| Setting | Default |
| --- | --- |
| Enable player skin requests | `true` |
| Maximum pending requests per player | `10` |
| Enable direct Workshop imports | `true` |
| Enable Workshop collection imports | `true` |
| Workshop request timeout seconds | `15` |
| Workshop import batch size (1-100) | `50` |
| Configured Workshop collection IDs | empty |

### Performance Settings

| Setting | Default |
| --- | --- |
| Maximum bulk entities/items processed per command | `2000` |
| Bulk operations per server tick | `32` |

## Data and database

RogueRust family data path: `RogueRust/RogueRustSkins/`.

The catalogue cache is stored in:

`data/RogueRust/RogueRustSkins/SkinCache.db`

The plugin registers a RogueRust SQLite connection named `roguerustskins-cache` and migrates tables `skin_cache` and `skin_cache_meta`. Player preferences, Workshop requests and imported Workshop data are also persisted by the plugin's RogueRust data routines.

## Runtime and performance notes

- Cached skins are usable while Steam inventory definitions are still loading.
- Live catalogue refresh publishes a signature-checked catalogue to RogueRust only when changed.
- Database persistence writes changed fingerprints and removes stale cache rows in batches.
- RogueRust player-skin/session caches are pruned every 10 minutes.
- Bulk work is capped and processed in per-tick batches.
- Browser page changes are debounced to avoid duplicate CUI submissions.

## Migration

Older numbered config section names (`01 - General`, etc.) are renamed to the current descriptive sections while preserving values.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustSkins.cs` in the plugin directory, grant only desired feature permissions, and allow the first catalogue/cache initialization to complete. Retain `SkinCache.db` and player data when updating unless release notes explicitly require regeneration.
