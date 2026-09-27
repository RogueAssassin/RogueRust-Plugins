# RogueRust Custom Player Messages

**Version:** `3.1.0`  
**Author:** RogueAssassin  
**Game:** Rust  
**Frameworks:** Oxide / Carbon  
**Required extension:** [Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)

Custom connect/disconnect messages and public player-list commands powered by RogueRust, with optional country lookup, privacy-aware player lists, persistent country caching and migrated localization.

## Features

- Configurable join and leave broadcasts
- Optional disconnect reasons
- Optional country name or country-code display on join
- Country lookup caching and failed-request throttling
- Privacy permission for hidden players
- Admin visibility of hidden players
- `/online`, `/players` and `/who` player-list commands
- Legacy config, data and language migration

## Compatibility

Designed for supported Rust servers running **Oxide** or **Carbon** with the RogueRust extension installed.

## Dependencies

### Required

- **[Oxide.Ext.RogueRust](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases)**

The default country lookup uses the free `ip-api.com` HTTP endpoint. Country display can be disabled if external lookup is not wanted.

## Permissions

- `roguerustcustomplayermessages.hidden` — hides the player from join/leave broadcasts and public player listings.
- `roguerustcustomplayermessages.admin` — allows a player to see hidden players in player-list commands. Native admins can also see hidden players.

## Commands

### `/online`
Canonical command: `roguerust.online`. Shows the number of visible online players. Console use is supported. Cooldown: 1 second.

### `/players`
Canonical command: `roguerust.players`. Lists visible online players. Alias: `/who`. Console use is supported. Cooldown: 1 second.

## Configuration

Configuration is stored in `CustomPlayerMessages.json` / the framework-generated config for the plugin name.

### Message Settings

- **Show join message** — default `true`.
- **Show leave message** — default `true`.
- **Show disconnect reason** — default `true`.
- **Show country in join message** — default `true`.
- **Use country code instead of country name** — default `false`.

### Country Lookup Settings

- **Country lookup timeout in seconds** — default `5`; constrained to `1–30` seconds.
- **Country cache lifetime in hours** — default `24`; constrained to `1–168` hours.
- **Failed country lookup cache lifetime in minutes** — default `5`; constrained to `1–60` minutes.
- **Minimum seconds between requests to the country lookup host** — default `0.25`; constrained to `0–10` seconds.
- **Country lookup URL** — default `http://ip-api.com/json/{0}?fields=status,message,country,countryCode`. The `{0}` address placeholder is required.

## Default Configuration

```json
{
  "Message Settings": {
    "Show join message": true,
    "Show leave message": true,
    "Show disconnect reason": true,
    "Show country in join message": true,
    "Use country code instead of country name": false
  },
  "Country Lookup Settings": {
    "Country lookup timeout in seconds": 5.0,
    "Country cache lifetime in hours": 24,
    "Failed country lookup cache lifetime in minutes": 5,
    "Minimum seconds between requests to the country lookup host": 0.25,
    "Country lookup URL (HTTP is required by ip-api's free endpoint)": "http://ip-api.com/json/{0}?fields=status,message,country,countryCode"
  },
  "Version (DO NOT CHANGE)": "3.1.0"
}
```

## Stored Data

Country results are persisted under the RogueRust data layout at `RogueRust/RogueRustCustomPlayerMessages/country-cache.json`. IP addresses are not stored directly as cache keys; the plugin hashes the address before caching the country result and expiry.

The previous `CustomPlayerMessages/country-cache` data location is migrated when the current cache is empty.

## Localization

Messages are stored at `lang/<language>/RogueRust/RogueRustCustomPlayerMessages/messages.json`. The plugin migrates supported legacy language locations and provides defaults for join, leave, local-network and player-list messages.

## Networking and Performance

Country requests use RogueRust HTTP services with a bounded 32 KiB response, configurable timeout and host interval, and two-attempt retry policy for timeout/rate-limit/server errors. Duplicate lookups for the same address are coalesced while a request is pending. Cache saves are debounced.

## Configuration Migration

The plugin automatically migrates the v3.0.0 flat configuration into the grouped RogueRust family layout and writes the current version back to disk.

## Installation

1. Install a supported Rust server with Oxide or Carbon.
2. Install the latest [Oxide.Ext.RogueRust DLL](https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases).
3. Download [RogueRustCustomPlayerMessages.cs](../plugins/RogueRustCustomPlayerMessages.cs).
4. Place it in the framework plugin directory.
5. Review the generated configuration and permissions.

## Updating

Replace the `.cs` file with the newer revision. Existing configuration, cache data and supported legacy language files are migrated where applicable. Do not manually change `Version (DO NOT CHANGE)`.

## Source

[View the plugin source](../plugins/RogueRustCustomPlayerMessages.cs)

[Back to documentation index](README.md) · [Back to plugin catalogue](../README.md)
