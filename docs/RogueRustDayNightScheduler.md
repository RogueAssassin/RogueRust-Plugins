# RogueRust Day/Night Scheduler

**Version:** `3.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

RogueRust-powered day/night duration scheduling, cycle skipping and protected time controls.

## Features

- Configurable real-time day and night lengths
- Manual time control
- Freeze/unfreeze world time
- Optional automatic day/night skipping
- Auth-level protected commands

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerustdaynightscheduler.use` — permits command use where permission access is accepted.

## Commands

- `tod`
- `tod.freeze`
- `tod.unfreeze`
- `daynight.daylength`
- `daynight.nightlength`

## Configuration

The current revision groups its configuration into:

- `Cycle Settings`
- `Permission Settings`
- `Time Control Settings`
- `Developer Settings`

Auth levels for duration commands and freeze/time commands are independently configurable.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustDayNightScheduler.cs](../plugins/RogueRustDayNightScheduler.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustDayNightScheduler.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
