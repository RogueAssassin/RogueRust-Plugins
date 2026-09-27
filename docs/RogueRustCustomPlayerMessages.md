# RogueRust Custom Player Messages

**Version:** `3.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Custom connect/disconnect messages and public player-list commands powered by RogueRust.

## Features

- Configurable join and leave messages
- Optional disconnect reasons
- Optional country lookup with caching
- Public online/player-list commands
- Hidden-player and admin permissions

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerustcustomplayermessages.hidden` — hides a player from public player lists/messages where applicable.
- `roguerustcustomplayermessages.admin` — administrative access for supported management behaviour.

## Commands

- `roguerust.online` — lists online players.
- `roguerust.players` — player-list command.

## Configuration

The current revision groups its configuration into:

- `Message Settings`
- `Country Lookup Settings`

Country lookup supports timeout, success/failure cache lifetimes, minimum request spacing and a configurable lookup URL.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustCustomPlayerMessages.cs](../plugins/RogueRustCustomPlayerMessages.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustCustomPlayerMessages.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
