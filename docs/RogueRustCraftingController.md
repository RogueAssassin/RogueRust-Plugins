# RogueRustCraftingController

> RogueRust-powered crafting control with legacy configuration and permission compatibility.

**Version:** `2.1.0`

[Plugin source](../plugins/RogueRustCraftingController.cs) · [Back to plugin catalogue](../README.md)

## Features
- Configurable crafting rates and item-specific crafting behaviour.
- Item blocking/unblocking controls.
- Workbench-level and craft-skin controls.
- Instant bulk-crafting permission support.
- RogueRust family configuration and compatibility handling.

## Compatibility
Rust · Oxide · Carbon · RogueRust extension/framework

## Permissions
`roguerustcraftingcontroller.blockitems`, `roguerustcraftingcontroller.craftingrate`, `roguerustcraftingcontroller.instantbulkcraft`, `roguerustcraftingcontroller.itemrate`, `roguerustcraftingcontroller.setbenchlvl`, `roguerustcraftingcontroller.setskins`.

## Commands
`benchlvl`, `blockitem`, `craftrate`, `crafttime`, `setcraftskin`, `unblockitem`

## Configuration
Configuration is generated in the normal Oxide/Carbon config directory. Existing configuration should normally be retained during upgrades so plugin migration logic can preserve settings.

## Installation
Install the RogueRust extension/framework, then place `RogueRustCraftingController.cs` in the server plugin directory.

## Updating
Replace the `.cs` file while retaining configuration/data unless a release specifically requires regeneration.
