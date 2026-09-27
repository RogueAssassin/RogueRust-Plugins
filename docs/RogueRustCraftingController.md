# RogueRustCraftingController

> RogueRust-powered crafting control with legacy configuration and permission compatibility.

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustCraftingController.cs) · [Back to plugin catalogue](../README.md)

## Features

- Global crafting-speed multiplier, including instant crafting.
- Permission-based bonus crafting multipliers.
- Advanced per-item crafting/research/workbench/craft-time controls.
- Item craft blocking/unblocking.
- Optional full-inventory crafting behaviour.
- Optional random skins and per-item default skin IDs.
- Optional instant bulk craft permission.
- Optional completion of queued crafting during shutdown.
- Legacy CraftingController configuration migration.
- Restores original blueprint values when unloaded.

## Permissions

- `roguerustcraftingcontroller.instantbulkcraft`
- `roguerustcraftingcontroller.blockitems`
- `roguerustcraftingcontroller.itemrate`
- `roguerustcraftingcontroller.craftingrate`
- `roguerustcraftingcontroller.setbenchlvl`
- `roguerustcraftingcontroller.setskins`
- Configured bonus tiers create `roguerustcraftingcontroller.<suffix>` permissions dynamically.

## Commands

The source provides administrative controls for blocking/unblocking items, changing item craft time, workbench level, default skin and the global crafting multiplier. Built-in language usage strings include `/crafttime item.shortname timetocraft`, `/blockitem item.shortname`, `/unblockitem item.shortname` and `/benchlvl item.shortname workbenchlvl`.

## Configuration

Configuration file: `config/RogueRustCraftingController.json`

### General Settings

| Setting | Default |
| --- | --- |
| Save commands to config | `true` |
| Simple Mode | `false` |
| Complete crafting on server shut down | `false` |
| Show Crafting Notes | `false` |

Simple Mode disables instant bulk craft, skin options and full-inventory checks for a lower-overhead runtime path.

### Permission Settings

Default bonus multipliers:

```json
{
  "vip1": 1.5,
  "vip2": 2.0
}
```

These become `roguerustcraftingcontroller.vip1` and `.vip2`.

### Crafting Settings

| Setting | Default |
| --- | --- |
| Default crafting rate multiplier 0 = Instant, 1 = Default, 2 = 2x speed | `1.0` |
| Allow crafting when inventory is full | `false` |
| Craft items with random skins if not already skinned | `false` |

The multiplier is clamped to `>= 0`. Values above `10` trigger a warning because old configurations may have used percentages.

### Advanced Crafting Options

The dictionary is keyed by item shortname. Each item contains:

- `canCraft`
- `canResearch`
- `useCustomCraftTime`
- `craftTime`
- `workbenchLevel`
- `defaultskinid`

Missing current Rust items are added automatically using live blueprint defaults.

### Representative default configuration

```json
{
  "General Settings": {
    "Save commands to config (save config changes via command to the configuration)": true,
    "Simple Mode (disables: instant bulk craft, skin options and full inventory checks for better performance)": false,
    "Complete crafting on server shut down": false,
    "Show Crafting Notes": false
  },
  "Permission Settings": {
    "Crafting rate bonus multiplier (permission suffix, multiplier)": {
      "vip1": 1.5,
      "vip2": 2.0
    }
  },
  "Crafting Settings": {
    "Default crafting rate multiplier 0 = Instant, 1 = Default, 2 = 2x speed": 1.0,
    "Allow crafting when inventory is full": false,
    "Craft items with random skins if not already skinned": false
  },
  "Advanced Crafting Options": {},
  "Version (DO NOT CHANGE)": "2.1.0"
}
```

## Data and localization

Family path: `RogueRust/RogueRustCraftingController/`  
Language path: `lang/<language>/RogueRust/RogueRustCraftingController/messages.json`

The current implementation does not require a separate persistent player database for its core crafting rules.

## Migration

`CraftingController.json` is copied to the RogueRust plugin filename when appropriate. Older flat configuration is converted into the grouped v2.1.0 layout.

## Performance notes

- Simple Mode unsubscribes advanced crafting hooks.
- Blueprint defaults are cached once and restored on unload.
- Permission multiplier arrays are built at initialization rather than repeatedly enumerating configuration.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustCraftingController.cs` in the plugin directory, review the generated config, and grant only the required permissions. Replace the `.cs` file for updates; supported legacy config is migrated automatically.
