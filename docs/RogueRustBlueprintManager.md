# RogueRustBlueprintManager

> RogueRust-powered blueprint management with legacy permission and command compatibility.

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustBlueprintManager.cs) · [Back to plugin catalogue](../README.md)

## Features

- Default and blacklisted blueprint sets.
- Permission-driven blueprint unlocks by custom set, workbench level, item category and DLC.
- `all` permission for all eligible blueprints.
- Optional automatic player blueprint refresh when permissions/groups change.
- Advanced per-blueprint researchability, scrap cost and default-blueprint control.
- Per-blueprint delayed unlock after wipe.
- Optional automatic unlock after the configured wipe delay.
- Map-wipe blueprint reset support.
- Batched player update queue to avoid updating every player in one frame.
- Legacy BlueprintManager config and language migration.

## Permissions

- `roguerustblueprintmanager.admin` — administrative blueprint commands.
- `roguerustblueprintmanager.all` — unlock all eligible blueprints.
- Runtime permissions are generated for `WorkbenchLvL*`, item categories, DLC and each configured custom permission set.

Custom permission keys under `Assign Custom Blueprint Unlocks To Permissions` become `roguerustblueprintmanager.<suffix>`.

## Commands

Administrative commands in the current source include `bpremove`, `bpreset`, `bpunlock`, `bpunlockall` and `bpwipeall`. Access is controlled by the plugin's admin permission/command metadata.

## Configuration

Configuration file: `config/RogueRustBlueprintManager.json`

### General Settings

| Setting | Default | Purpose |
| --- | --- | --- |
| Simple Mode (disables advanced blueprint management options) | `true` | Uses permission/default blueprint management without mutating every blueprint definition. |
| Wipe Blueprints With Map Wipe | `false` | Resets player blueprint state with a map wipe. |

### Permission Settings

| Setting | Default |
| --- | --- |
| Update Players On Permission Change | `true` |
| Assign Custom Blueprint Unlocks To Permissions | `customperm1: [rock]`, `customperm2: [torch]` |

### Blueprint Settings

- `Blacklist (items excluded from automatic learning)` — default empty.
- `Default Blueprints (automatically learned)` — default empty.

Use Rust item shortnames.

### Advanced Blueprint Settings

`Blueprint Management Options` is keyed by item shortname. Each entry supports:

| Setting | Default |
| --- | --- |
| Default Blueprint | blueprint's Rust default |
| Can Research | `true` / Rust default |
| Scrap Required | Rust blueprint value |
| Unlock Minutes After Wipe (-1 = disabled) | `-1` |
| Automatically Unlock After Wipe Delay | `false` |

When advanced mode is enabled, missing item entries are generated from current Rust blueprint definitions.

### Representative default configuration

```json
{
  "General Settings": {
    "Simple Mode (disables advanced blueprint management options)": true,
    "Wipe Blueprints With Map Wipe": false
  },
  "Permission Settings": {
    "Update Players On Permission Change": true,
    "Assign Custom Blueprint Unlocks To Permissions": {
      "customperm1": ["rock"],
      "customperm2": ["torch"]
    }
  },
  "Blueprint Settings": {
    "Blacklist (items excluded from automatic learning)": [],
    "Default Blueprints (automatically learned)": []
  },
  "Advanced Blueprint Settings": {
    "Blueprint Management Options": {}
  },
  "Version (DO NOT CHANGE)": "2.1.0"
}
```

## Data and localization

Runtime lifecycle identifies the family data location as `RogueRust/RogueRustBlueprintManager/`. Language files use:

`lang/<language>/RogueRust/RogueRustBlueprintManager/messages.json`

The plugin primarily derives blueprint state from config, permissions and Rust player blueprint state rather than maintaining a separate custom player database.

## Migration

- `BlueprintManager.json` and `Blueprint Manager.json` are recognized as legacy config filenames.
- The old flat configuration is migrated into General, Permission, Blueprint and Advanced groups.
- Existing administrator values are preserved where possible.

## Performance notes

Blueprint updates are queued and processed cooperatively instead of updating all connected players in one synchronous burst. Internal blacklist/default/permission blueprint sets use cached item IDs for runtime checks.

## Installation

1. Install the latest [Oxide.Ext.RogueRust release](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
2. Place `RogueRustBlueprintManager.cs` in the plugin directory.
3. Review the generated config before disabling Simple Mode.
4. Grant the desired blueprint/admin permissions.

## Updating

Replace the `.cs` file and retain the configuration. Supported legacy config shapes and filenames are migrated automatically.
