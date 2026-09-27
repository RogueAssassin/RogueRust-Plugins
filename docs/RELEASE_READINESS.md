# RogueRust Plugins — Release Readiness

This checklist is the release gate for the public RogueRust plugin collection. A plugin should not be described as release-ready until its current source revision and documentation have both been checked.

## Automated static gate

The repository now runs `.github/workflows/release-gate.yml` whenever plugin sources, documentation, the catalogue, or the gate itself changes. It executes `scripts/release_gate.py` and fails when the repository has source/documentation drift.

The automated gate verifies:

- every plugin source has a matching documentation page;
- `[Info]` metadata is parseable and the source version is represented in the catalogue and plugin page;
- root catalogue source links exist for every plugin;
- every plugin page links back to the correct `.cs` source;
- every plugin page contains the RogueRust DLL release link;
- every plugin page contains the required baseline sections: Features, Compatibility, Permissions, Commands, Configuration, Installation and Updating;
- RogueRust documentation pages do not exist without a matching plugin source.

Run the same gate locally with:

```bash
python scripts/release_gate.py
```

> Passing this workflow means the repository is structurally consistent. It does **not** replace Oxide/Carbon compile/load tests or in-game functional smoke tests.

## Documentation standard

Each plugin page must include, where applicable:

- current plugin version and source link;
- Oxide / Carbon compatibility statement;
- required RogueRust DLL release link;
- feature summary derived from current source;
- all registered permissions with purpose;
- all chat/console/RogueCommand names, aliases, usage and access requirements;
- every configuration group and setting with default value and validation/range notes;
- a representative default JSON configuration;
- persistent data paths and migration behaviour;
- localization paths and migration behaviour;
- optional plugin/service integrations;
- hooks or developer API exposed to other plugins;
- performance/runtime notes where behaviour is non-trivial;
- installation and updating instructions.

## Repository release gate

Before tagging a public collection release:

- [x] Automated source/documentation consistency gate is present and runs on relevant pushes/PRs.
- [ ] Every `.cs` file in `plugins/` has a matching page in `docs/` and passes the automated gate.
- [ ] README catalogue versions match `[Info]` versions in source and pass the automated gate.
- [ ] Every documentation page links to the correct source file and passes the automated gate.
- [ ] Every plugin that requires RogueRust links to https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases and passes the automated gate.
- [ ] Commands and permissions have been checked against the current source revision.
- [ ] Configuration defaults/examples have been checked against the current source revision.
- [ ] Data and language paths have been checked against the current source revision.
- [ ] Optional dependencies/integrations are clearly marked optional.
- [ ] No documentation claims a dependency or feature that is absent from source.
- [ ] Plugins have been compile/load tested on the intended current Oxide and Carbon environments.
- [ ] High-impact plugins have received functional smoke tests for their primary commands/UI/gameplay paths.
- [ ] Repository licensing/reuse terms are intentionally decided before the first formal public release.
- [ ] A release changelog is prepared from the exact plugin revisions being published.

## Deep-documentation progress

Source-verified full-standard pages completed so far:

- [RogueRustFuelStackOptimizer](RogueRustFuelStackOptimizer.md)
- [RogueRustCustomPlayerMessages](RogueRustCustomPlayerMessages.md)
- [RogueRustDayNightScheduler](RogueRustDayNightScheduler.md)
- [RogueRustEventDirector](RogueRustEventDirector.md)

The remaining plugin pages must reach the same source-level standard before the collection is marked fully release-ready. The automated gate prevents basic catalogue/version/link/section drift while that deeper verification is completed.

## Runtime release gates

For the final release candidate, validate on clean/current test servers:

1. **Oxide:** compile and load every plugin with the current RogueRust DLL.
2. **Carbon:** compile and load every plugin with the same release DLL.
3. Confirm no startup exceptions, missing methods, duplicate command registration, or dependency errors.
4. Smoke-test high-impact UI/gameplay paths, especially Admin Menu, Furnace Splitter, Grid Power, Removal Tool, Skins, Rando Spawns and Server Restarter.
5. Record the exact DLL release and plugin source revisions used for the successful test.

[Documentation index](README.md) · [Plugin catalogue](../README.md)
