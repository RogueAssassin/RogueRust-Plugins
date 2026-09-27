# RogueRustHeliLeashControl

> Keeps a heavily damaged Patrol Helicopter near its last valid attacker.

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

[Plugin source](../plugins/RogueRustHeliLeashControl.cs) · [Back to plugin catalogue](../README.md)

## Features

- Activates leash behaviour below a configurable projected helicopter-health threshold.
- Tracks the last valid player attacker.
- Redirects a Patrol Helicopter when it exceeds the configured distance.
- Configurable check interval.
- Optional global redirect message with player and map-grid placeholders.
- Per-helicopter message cooldown.
- Optional debug logging.
- Runtime tracking is removed when the helicopter dies or the tracked attacker is no longer valid.

## Permissions and commands

No player permissions or public commands are required in the current revision. Operation is automatic and event-driven.

## Configuration

Configuration file: `config/RogueRustHeliLeashControl.json`

### Leash Settings

| Setting | Default | Validation |
| --- | ---: | --- |
| Enable Leash Behavior | `true` | — |
| Health Threshold to Enable Leash | `400` | `1-10000` |
| Maximum Allowed Distance From Last Attacker | `150` | `25-2000` |
| Leash Check Interval in Seconds | `1` | `0.25-10` |

### Message Settings

| Setting | Default | Validation |
| --- | --- | --- |
| Send Global Message When Helicopter Is Redirected | `true` | — |
| Minimum Seconds Between Global Messages Per Helicopter | `30` | `5-600` |
| Global Message Format (`{0}=player`, `{1}=grid`) | helicopter/player/grid message | non-empty fallback applied |

### Developer Settings

`Enable Debug Messages in Console`: `false`

### Default configuration

```json
{
  "Leash Settings": {
    "Enable Leash Behavior": true,
    "Health Threshold to Enable Leash": 400.0,
    "Maximum Allowed Distance From Last Attacker": 150.0,
    "Leash Check Interval in Seconds": 1.0
  },
  "Message Settings": {
    "Send Global Message When Helicopter Is Redirected": true,
    "Minimum Seconds Between Global Messages Per Helicopter": 30.0,
    "Global Message Format ({0}=player, {1}=grid)": "🚁 <color=#ff4d4d>Helicopter is staying close to {0} at [<color=#ffd700>{1}</color>]</color>"
  },
  "Developer Settings": {
    "Enable Debug Messages in Console": false
  },
  "Version (DO NOT CHANGE)": "2.1.0"
}
```

## Data and localization

Persistent custom data: **none**.  
Custom language files: **none**.  
All tracked helicopter/attacker state is runtime-only.

## Migration

Older flat HeliLeashControl keys are migrated into the grouped Leash, Message and Developer sections, then validated/clamped.

## Performance notes

Damage hooks only begin tracking a Patrol Helicopter after projected health crosses the configured threshold. The periodic leash check runs at the configured interval and only iterates currently tracked helicopters.

## Installation and updating

Install the latest [RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases), place `RogueRustHeliLeashControl.cs` in the plugin directory and review the generated thresholds/messages. Replace the `.cs` file for updates; legacy config is migrated automatically.
