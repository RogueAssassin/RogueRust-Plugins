# RogueRust Plugins — Release Readiness

This checklist is the release gate for the public RogueRust plugin collection. A plugin should not be described as fully release-ready until its current source revision, documentation and runtime behaviour have all been checked.

## Automated static gate

The repository runs `.github/workflows/release-gate.yml` whenever plugin sources, documentation, the catalogue, or the gate itself changes. It executes `scripts/release_gate.py` and fails when the repository has source/documentation drift.

The automated gate verifies source/documentation matching, `[Info]` version representation, catalogue/source links, RogueRust DLL links, required baseline sections and orphan documentation.

Run locally with:

```bash
python scripts/release_gate.py
```

> Passing the static workflow means the repository is structurally consistent. It does **not** replace Oxide/Carbon compile/load tests or in-game functional smoke tests.

## Documentation standard

Every plugin page has now been passed against its current source and expanded to document, where applicable: current version/source, Oxide/Carbon compatibility, RogueRust DLL dependency, features, permissions, commands, configuration/defaults/validation, representative examples, data and language paths, migrations, optional integrations, developer API/hooks, performance/runtime behaviour, installation and updating.

## Repository release gate

Before tagging a public collection release:

- [x] Automated source/documentation consistency gate is present and runs on relevant pushes/PRs.
- [x] Every `.cs` file in `plugins/` has a matching page in `docs/`.
- [x] README catalogue versions and documentation versions are covered by the automated source metadata gate.
- [x] Every documentation page links to its plugin source.
- [x] RogueRust-dependent plugin pages link to https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases.
- [x] Commands and permissions have received the source-level documentation pass.
- [x] Configuration groups/defaults and important validation rules have received the source-level documentation pass.
- [x] Data and language paths have received the source-level documentation pass where applicable.
- [x] Optional dependencies/integrations are identified as optional.
- [x] Documentation has been reviewed against current source rather than legacy plugin descriptions.
- [ ] Plugins have been compile/load tested on the intended current Oxide environment.
- [ ] Plugins have been compile/load tested on the intended current Carbon environment.
- [ ] High-impact plugins have received functional smoke tests for their primary commands/UI/gameplay paths.
- [ ] Repository licensing/reuse terms are intentionally decided before the first formal public release.
- [ ] A release changelog is prepared from the exact plugin revisions being published.

## Deep-documentation progress

Full source-level documentation pass completed for all current plugin pages:

- [RogueRustAdminMenu](RogueRustAdminMenu.md)
- [RogueRustBlueprintManager](RogueRustBlueprintManager.md)
- [RogueRustCraftingController](RogueRustCraftingController.md)
- [RogueRustCustomPlayerMessages](RogueRustCustomPlayerMessages.md)
- [RogueRustDayNightScheduler](RogueRustDayNightScheduler.md)
- [RogueRustDeathNotes](RogueRustDeathNotes.md)
- [RogueRustEventDirector](RogueRustEventDirector.md)
- [RogueRustFuelStackOptimizer](RogueRustFuelStackOptimizer.md)
- [RogueRustFurnaceSplitter](RogueRustFurnaceSplitter.md)
- [RogueRustGridPower](RogueRustGridPower.md)
- [RogueRustHackableCrateTracker](RogueRustHackableCrateTracker.md)
- [RogueRustHeliLeashControl](RogueRustHeliLeashControl.md)
- [RogueRustRandoSpawns](RogueRustRandoSpawns.md)
- [RogueRustRemovalTool](RogueRustRemovalTool.md)
- [RogueRustServerRestarter](RogueRustServerRestarter.md)
- [RogueRustSkins](RogueRustSkins.md)
- [RogueRustTurretLimitOverride](RogueRustTurretLimitOverride.md)

## Runtime release gates

For the final release candidate, validate on clean/current test servers:

1. **Oxide:** compile and load every plugin with the intended RogueRust release DLL.
2. **Carbon:** compile and load every plugin with the same release DLL.
3. Confirm no startup exceptions, missing methods, duplicate command registration or dependency errors.
4. Smoke-test high-impact UI/gameplay paths, especially Admin Menu, Furnace Splitter, Grid Power, Removal Tool, Skins, Rando Spawns and Server Restarter.
5. Verify configuration generation/migration on a clean install and at least one representative upgrade path.
6. Record the exact DLL release and plugin source revisions used for the successful test.
7. Prepare the release changelog and decide repository licensing/reuse terms before tagging the formal public release.

[Documentation index](README.md) · [Plugin catalogue](../README.md)
