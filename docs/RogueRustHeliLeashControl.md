# RogueRust Heli Leash Control

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Keeps a heavily damaged Patrol Helicopter near its last valid attacker.

## Features

- Health-threshold based leash activation
- Maximum attacker distance
- Configurable leash check interval
- Optional global redirect messages
- Per-helicopter message cooldown
- Debug logging

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- No player permission is required in the current revision.

## Commands

- No public RogueCommand handlers are defined in the current revision.

## Configuration

The current revision groups its configuration into:

- `Leash Settings`
- `Message Settings`
- `Developer Settings`

The global message format supports `{0}` for player and `{1}` for map grid.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustHeliLeashControl.cs](../plugins/RogueRustHeliLeashControl.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustHeliLeashControl.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
