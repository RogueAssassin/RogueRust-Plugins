# RogueRustGridPower

> RogueRust-native GridPower controller for automatic world streetlights, deterministic density, diagnostics, and player-facing grid events.

**Version:** `1.8.0`

[Plugin source](../plugins/RogueRustGridPower.cs) · [Back to plugin catalogue](../README.md)

## Features
- Automatic public grid and streetlight control.
- Deterministic density selection and infrastructure discovery.
- Player-facing grid events and extensive diagnostics.
- Performance-focused cached world discovery.

## Compatibility
Rust · Oxide · Carbon · RogueRust extension/framework

## Permissions
- `roguerustgridpower.admin`

## Commands
`rrgrid.apply`, `rrgrid.climb.inspect`, `rrgrid.climb.remove`, `rrgrid.climb.test`, `rrgrid.climb.teststatus`, `rrgrid.debug`, `rrgrid.grid`, `rrgrid.help`, `rrgrid.infrastructure`, `rrgrid.inspect`, `rrgrid.playergrid`, `rrgrid.power.inspect`, `rrgrid.power.remove`, `rrgrid.power.test`, `rrgrid.powerprobe`, `rrgrid.rebuild`, `rrgrid.rebuild.status`, `rrgrid.refresh`, `rrgrid.scan`, `rrgrid.status`.

## Configuration
Configuration is generated in the standard framework config directory. Density, scheduling and diagnostic behaviour should be managed through the plugin's grouped configuration rather than source edits.

## Installation
Install RogueRust, copy `RogueRustGridPower.cs` to the plugin directory and review the generated configuration before enabling world-grid automation.
