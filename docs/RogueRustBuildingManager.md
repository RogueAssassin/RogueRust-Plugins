# RogueRustBuildingManager

> Upgrade, downgrade, skin and wallpaper a whole base, or grade blocks as you place or hammer them.

**Version:** `1.2.0`  
**Author:** RogueAssassin  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases) 4.4.63 or newer

[Plugin source](../plugins/RogueRustBuildingManager.cs) · [Back to plugin catalogue](../README.md)

## Recent Changes

- **1.2.0:** wallpaper now costs the game's own price per face, taken up front with anything unused returned, and is covered by undo. `Charge The Game's Price For Each Face Papered` under Wallpaper turns the cost off.
- A config file that cannot be read is copied to `<name>.json.invalid` before the defaults are written, so a typo no longer costs every setting.
- **1.1.1:** each wallpaper is listed once. The wall and ceiling lists were being filled twice. A wallpaper put on a face that turns out to be outdoors is taken back off by the game's own check.
- **1.1.0:** the menu is drawn in sections, so a click only resends the part that changed. Wallpapers are chosen from a list by name instead of stepping through them one at a time. The upgrade effect is skipped on large operations.

## Features

- **Whole-base upgrade and downgrade** to a chosen grade, from chat or the menu. Works on the building you are looking at, otherwise the one whose tool cupboard covers you.
- **Auto grade** — blocks you place become your chosen grade. Switches itself off after a quiet spell.
- **Hammer grading** — hit a block to take it to your chosen grade. A block already at that grade gets a normal hit, so it can still be repaired.
- **Skins** — a skin per grade, used when upgrading, when you upgrade by hand, or applied to the whole base.
- **Wallpaper** — one each for walls, floors and ceilings, picked from a list, then applied to or stripped from the whole base. The list is read from the game, so new wallpapers appear without a plugin update.
- **Undo** — the last upgrade, downgrade, skin or wallpaper operation can be reversed for a short while, with resources returned.
- **Fair costs** — upgrades charge the game's own price, all or nothing, and tell you what you are short of. Anything paid for a block that is then skipped is returned.
- Work is spread over server ticks so a large base does not stall the server.

## Permissions

- `roguerustbuildingmanager.use` — the menu, auto grade and hammer grading.
- `roguerustbuildingmanager.bulk` — whole-base upgrade and downgrade.
- `roguerustbuildingmanager.skins` — choose and apply building skins.
- `roguerustbuildingmanager.wallpaper` — choose, apply and remove wallpaper.
- `roguerustbuildingmanager.nocost` — upgrades are free.
- `roguerustbuildingmanager.admin` — all of the above except free upgrades, on any building, without tool cupboard authorisation.

## Commands

| Command | Description |
| --- | --- |
| `/bm` | Opens the menu. Aliases: `/building`, `/bgrade`. |
| `/up [wood\|stone\|metal\|hqm]` | Upgrades the whole building. Uses your chosen grade when none is given. |
| `/down [twig\|wood\|stone\|metal]` | Downgrades the whole building. Uses one step below your chosen grade when none is given. |
| `/bm grade <grade>` | Sets your chosen grade. |
| `/bm auto` | Turns auto grade on or off. |
| `/bm hammer` | Turns hammer grading on or off. |
| `/bm skins` | Applies your chosen skins to the whole building. |
| `/bm wallpaper` | Applies your chosen wallpapers. `/bm wallpaper off` removes them. |
| `/bm undo` | Reverses your last operation. |

## Configuration

Configuration file: `config/RogueRustBuildingManager.json`

### General

| Setting | Default |
| --- | --- |
| Blocks Changed Per Server Tick (Lower = Smoother, Higher = Faster) | `25` |
| Most Blocks One Operation May Touch | `6000` |
| Skip Blocks Attacked Within This Many Seconds (0 = Off) | `30` |
| Play The Upgrade Effect On Each Block | `true` |
| Skip The Effect When An Operation Touches More Than This Many Blocks (Keeps Big Bases Smooth) | `40` |
| Seconds Between Whole-Base Operations Per Player | `5` |
| Chat Prefix | `[Building]` in cyan |

### Access

| Setting | Default |
| --- | --- |
| Require Tool Cupboard Authorisation On The Building | `true` |
| Block While Raid-Blocked (Needs The NoEscape Plugin) | `true` |

### Costs

| Setting | Default |
| --- | --- |
| Charge Resources For Upgrades | `true` |
| Refund Resources When Downgrading | `false` |
| Downgrade Refund Percentage (0-100) | `50` |

Refunds are off by default. With them on, a player who upgrades for free could hand resources to a teammate through repeated downgrades; players with `nocost` never receive a refund.

### Auto Grade, Hammer Tool, Wallpaper, Undo

Each has an `Enabled` switch. In addition:

- **Wallpaper** — `Charge The Game's Price For Each Face Papered`, default `true`.
- **Auto Grade** — `Turn Itself Off After This Many Seconds Without Building (0 = Never)`, default `300`.
- **Undo** — `Seconds An Operation Can Be Undone For`, default `120`.

### Building Skins

| Setting | Default |
| --- | --- |
| Enabled | `true` |
| Only Offer Skins The Player Owns | `true` |
| Apply The Player's Chosen Skin When They Upgrade By Hand | `true` |

Building skins are paid game content. Leave ownership checking on unless you have decided otherwise for your server.

## Wallpaper notes

- Each face papered costs the game's own wallpaper price. The total is taken up front and anything unused is returned. Players with `nocost` pay nothing.
- Wallpaper operations can be undone like any other.
- Floors take a floor wallpaper on top and a ceiling wallpaper underneath.
- The second face of a wall, and the underside of a floor, must be indoors. The game checks this itself and removes paper from a face that is outside.

## Data and localization

- `RogueRustBuildingManager/players` — each player's chosen grade, toggles, skins and wallpapers.
- `lang/<language>/RogueRustBuildingManager.json` — every message and menu label.

## Works with

- **RogueRustSkins** 2.3.2+ hands structure skinning to this plugin when it is installed, using the skins chosen in the Skins menu.
- **RogueRustServerInfo** 1.1.2+ lists `/bm`, `/up` and `/down`.
- **NoEscape** — optional, for the raid-block rule.

Other plugins can call `OpenBuildingManager(BasePlayer)` and `ApplyBuildingSkins(BasePlayer, Dictionary<int, ulong>)`.

## Runtime and performance notes

- Finding buildings, pricing, payment and applying changes are done by the `Rogue.Buildings` service in the RogueRust DLL.
- The menu frame is sent once; each click resends only the sections that changed.
- Nothing runs on a timer while no operation is in progress.

## Installation

1. Install [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases) 4.4.63 or newer. The plugin will not compile on an older DLL.
2. Place `RogueRustBuildingManager.cs` in the plugin directory.
3. Grant `roguerustbuildingmanager.use` and whichever of `bulk`, `skins` and `wallpaper` you want players to have.

## Updating

Replace the `.cs` file and keep the config and data.
