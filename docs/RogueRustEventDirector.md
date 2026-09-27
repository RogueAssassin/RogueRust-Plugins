# RogueRust Event Director

**Version:** `1.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Performance-focused world event scheduling and CH47 crate direction powered by RogueRust.

## Features

- Schedules major Rust world events
- Per-event enable/disable controls
- Configurable min/max spawn intervals and concurrency
- Optional vanilla spawn suppression
- CH47 crate drop director with water and monument avoidance
- Persistent schedule data

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerusteventdirector.admin` — administrative Event Director commands.

## Commands

- `rred.status`
- `rred.spawn`
- `rred.reschedule`

## Configuration

The current revision groups its configuration into:

- `Scheduler Settings`
- `Cargo Plane`
- `Patrol Helicopter`
- `Bradley APC`
- `CH47 Chinook`
- `Cargo Ship`

CH47 drop direction supports minimum crate spacing, water clearance, monument avoidance and monument radius controls.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustEventDirector.cs](../plugins/RogueRustEventDirector.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustEventDirector.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
