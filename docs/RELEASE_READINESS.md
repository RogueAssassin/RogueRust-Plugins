# RogueRust Plugins — Release Readiness

This checklist is the release gate for the public RogueRust plugin collection. A plugin should not be described as release-ready until its current source revision and documentation have both been checked.

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

- [ ] Every `.cs` file in `plugins/` has a matching page in `docs/`.
- [ ] README catalogue versions match `[Info]` versions in source.
- [ ] Every documentation page links to the correct source file.
- [ ] Every plugin that requires RogueRust links to https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases.
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

The following pages have been upgraded to the full release-documentation standard in the current pass:

- [RogueRustFuelStackOptimizer](RogueRustFuelStackOptimizer.md)
- [RogueRustCustomPlayerMessages](RogueRustCustomPlayerMessages.md)
- [RogueRustDayNightScheduler](RogueRustDayNightScheduler.md)
- [RogueRustEventDirector](RogueRustEventDirector.md)

The remaining plugin pages should be upgraded and source-verified before the collection is marked fully release-ready.

[Documentation index](README.md) · [Plugin catalogue](../README.md)
