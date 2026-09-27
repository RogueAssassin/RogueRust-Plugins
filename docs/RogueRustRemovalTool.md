# RogueRustRemovalTool

> RogueRust-native building and deployable removal tool.

**Version:** `2.0.0`

[Plugin source](../plugins/RogueRustRemovalTool.cs) · [Back to plugin catalogue](../README.md)

## Features
- Building and deployable removal with RogueRust services.
- In-game removal UI.
- Permission-scoped normal, structure, external, target, override and admin removal modes.
- Optional economy/reward and relationship integrations.

## Compatibility
Rust · Oxide · Carbon · RogueRust extension/framework

### Optional integrations
`BuildingOwners`, `Clans`, `Economics`, `Friends`, `NoEscape`, `ServerRewards`.

## Permissions
`roguerustremovaltool.admin`, `roguerustremovaltool.all`, `roguerustremovaltool.external`, `roguerustremovaltool.normal`, `roguerustremovaltool.override`, `roguerustremovaltool.structure`, `roguerustremovaltool.target`.

## Commands
- `remove`

## Configuration
The generated configuration controls removal behaviour, access and optional integrations. Retain configuration when upgrading unless a release explicitly requires regeneration.

## Installation
Install RogueRust, copy `RogueRustRemovalTool.cs` into the plugin directory, configure the desired removal rules/integrations and grant only required permissions.
