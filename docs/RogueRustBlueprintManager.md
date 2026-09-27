# RogueRustBlueprintManager

> RogueRust-powered blueprint management with legacy permission and command compatibility.

**Version:** `2.1.0`

[Plugin source](../plugins/RogueRustBlueprintManager.cs) · [Back to plugin catalogue](../README.md)

## Features
- Blueprint management with RogueRust integration.
- Legacy configuration/permission compatibility and migration support.
- Default, blacklist, workbench, category and custom permission blueprint sets.
- Optional wipe-delayed blueprint unlock handling.

## Compatibility
Rust · Oxide · Carbon · RogueRust extension/framework

## Permissions
- `roguerustblueprintmanager.admin`
- `roguerustblueprintmanager.all`
- Runtime blueprint permissions are also generated for configured/custom sets, workbench levels, categories and DLC where applicable.

## Commands
`bpremove`, `bpreset`, `bpunlock`, `bpunlockall`, `bpwipeall`

## Configuration
The grouped configuration includes General Settings, Permission Settings, Blueprint Settings, Advanced Blueprint Settings and `Version (DO NOT CHANGE)`. Legacy BlueprintManager configuration is migrated where possible.

## Installation
Install the RogueRust extension/framework, then place `RogueRustBlueprintManager.cs` in the server plugin directory.

## Updating
Replace the `.cs` file and retain existing configuration/data; supported legacy layouts are migrated by the plugin.
