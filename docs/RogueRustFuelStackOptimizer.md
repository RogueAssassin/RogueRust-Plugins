# RogueRust Fuel Stack Optimizer

**Version:** `2.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Optimises dedicated Rust fuel containers through cached discovery using native Oxide/Carbon hooks.

## Features

- Dedicated fuel-container targeting
- Global and per-entity stack overrides
- Owner whitelist/blacklist filters
- Batched discovery and stack updates
- Configurable rescans
- Startup audit and fuel diagnostics

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

## Permissions

- `roguerustfuelstackoptimizer.admin` — administrative diagnostics and control commands.

## Commands

- `fuelstack.status`
- `fuelstack.audit`
- `fuelstack.fuels`
- `fuelstack.rescan`
- `fuelstackaudit`

## Configuration

The current revision groups its configuration into:

- `General Settings`
- `Container Settings`
- `Owner Filter Settings`
- `Performance Settings`
- `Developer Settings`

Overrides can target NetID, short prefab name or full prefab path.

The source file remains the authority for exact defaults, migrations and framework-specific behaviour.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Download and install the latest **[Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**.
3. Download the plugin from [RogueRustFuelStackOptimizer.cs](../plugins/RogueRustFuelStackOptimizer.cs).
4. Place it in your framework's plugins directory.
5. Review the generated configuration and grant only the permissions you need.

## Updating

Replace the plugin `.cs` with the newer revision. Existing configuration is retained unless the plugin's migration logic or release notes state otherwise.

## Source

[View the plugin source](../plugins/RogueRustFuelStackOptimizer.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
