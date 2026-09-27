# RogueRust Death Notes

**Version:** `2.2.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Death notification engine with native GUI output, optional Notify/UINotify integration and per-player controls.

## Features

- Native GUI death notifications
- Chat and console output
- Optional Notify and UINotify output
- Per-player `/dn` controls
- Patrol Helicopter and Bradley tag messages
- Configurable formatting, colors and message radius

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerustdeathnotes.cansee` — permits viewing when permission-gated output is enabled.
- `roguerustdeathnotes.cantsee` — suppresses death-note output for the player.
- `roguerustdeathnotes.suppress` — suppresses messages involving the player.
- `roguerustdeathnotes.seeteamonly` — restricts viewing to team-related events.

## Commands

- `dn` — player death-note controls.

## Configuration

The current revision groups its configuration into:

- `Formatting Settings`
- `Output Settings`
- `Player Control Settings`
- `Patrol Helicopter Settings`
- `Bradley APC Settings`
- `General Settings`
- `Developer Settings`

Optional integrations: `Notify` and `UINotify`. Native GUI output works without either integration.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustDeathNotes.cs](../plugins/RogueRustDeathNotes.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustDeathNotes.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
