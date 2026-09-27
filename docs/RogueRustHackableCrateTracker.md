# RogueRust Hackable Crate Tracker

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Tracks hackable crates, hackers, looters, map grids, world metadata and Discord notifications.

## Features

- Hack attempt tracking
- Hack completion tracking
- First actual looter tracking
- In-game and console reporting
- Discord webhook embeds
- Grid, coordinate and world metadata support

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerusthackablecratetracker.use` — receives permitted in-game reports when restriction is enabled.
- `roguerusthackablecratetracker.admin` — administrative permission.

## Commands

- No public RogueCommand handlers are defined in the current revision.

## Configuration

The current revision groups its configuration into:

- `Reporting Settings`
- `Discord Settings`
- `Location Settings`

Discord output uses a webhook URL directly; no separate Discord plugin is required.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustHackableCrateTracker.cs](../plugins/RogueRustHackableCrateTracker.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustHackableCrateTracker.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
