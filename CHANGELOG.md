# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [v1.5.0]

### Added

- **Dynamic kill points**: killing a stronger player pays more, killing a weaker one pays less
  - New `Points.DynamicKillPoints`, `DynamicKillPointsMin` and `DynamicKillPointsMax`, off by default
  - The death side keeps its own `DynamicDeathPoints*` options, so either can be tuned alone
- **Table suffix**: new `Database.TableSuffix` option gives `lvl_base_awp`, and pairs with `TablePrefix` for `awp_lvl_base_2v2`

### Changed

- **Dynamic death points are now off by default**: set `Points.DynamicDeathPoints` to `true` to keep scaling the death penalty by the point gap
- **`Database.TablePrefixPosition` replaced by `TableSuffix`**: a config that still sets it alongside a prefix is rejected at startup
  - If you used `"after"`, move the value to `TableSuffix` and clear `TablePrefix`; existing tables keep their names
- **`TablePrefix` now adds the separating underscore for you**, so `"awp"` and `"awp_"` both give `awp_lvl_base`
  - A prefix that did not already end in `_` names different tables now; startup logs a warning with both names

### Fixed

- **Dynamic death points were backwards**: being killed by a weaker player cost less instead of more
- **Dynamic multipliers swung to the cap on a small point gap**; an evenly matched kill now scores the configured `Kill` / `Death` value exactly

### Usage Example

**Points (`config.json`)** - both toggles are `false` by default:

```jsonc
{
  "K4Ranks": {
    // ... other settings ...
    "Points": {
      "DynamicDeathPoints": true,     // Dying to a weaker player costs more
      "DynamicDeathPointsMin": 0.5,   // Death multiplier vs the strongest possible attacker
      "DynamicDeathPointsMax": 3.0,   // Death multiplier vs the weakest possible attacker
      "DynamicKillPoints": true,      // Killing a stronger player pays more
      "DynamicKillPointsMin": 0.5,    // Kill multiplier vs the weakest possible victim
      "DynamicKillPointsMax": 3.0     // Kill multiplier vs the strongest possible victim
    }
  }
}
```

With the stock `Kill` 8 / `Death` -5: an even matchup scores 8 / 5, a 50-point player killing a 10000-point one earns 24 while the victim loses 15, and the reverse pays 4 for a loss of 3.

**Tables (`config.json`)** - both affixes are empty by default:

```jsonc
{
  "K4Ranks": {
    // ... other settings ...
    "Database": {
      "Connection": "host",
      "TablePrefix": "awp",    // awp_lvl_base, awp_lvl_base_settings, ...
      "TableSuffix": "2v2"     // awp_lvl_base_2v2 - both sides can be set at once
    }
  }
}
```

## [v1.4.0]

### Added

- **Table prefix position**: new `Database.TablePrefixPosition` option - `"before"` (default) gives `awp_lvl_base`, `"after"` gives `lvl_base_awp`
  - `"after"` adds the separating underscore for you, so `"awp"` and `"awp_"` behave the same
  - Invalid values are rejected at startup instead of quietly using a different set of tables

### Changed

- **Lower server CPU usage**: hot paths do far less work each tick, with no change in behaviour
  - Scoreboard ranks are resolved ahead of time and only re-resolved when something actually changes
  - Each player's SteamID and bot flag are read once per connection and reused by every stat handler
  - Player names are only built when a point message will actually show them
- **Saving rebuilt around a single background writer** (see README "Saving")
  - Saves every 60 s, at round end, at match end and on disconnect - never two at once
  - Each player is saved in one transaction; a failed save is retried instead of dropped
  - Changes made while a save is running are kept for the next one instead of being lost
  - Unload waits at most 8 seconds for the final save instead of hanging on an unreachable database
- **Faster leaderboards**: `!top` and `!toptime` query only the rows they display instead of reading the whole table

### Fixed

- **SQLite never worked**: a fresh install created no tables and disabled the plugin; all three engines now work as documented
- **PostgreSQL skipped three migrations**, including the SteamID conversion
- **Duplicate weapon stats** on existing LVL Ranks databases: a new migration adds the key the save path needs
- **Shared hosting**: a migration could pick up a same-named table from another database on the server
- **Progress lost on disconnect**: players who disconnected while dead or spectating were never saved
- **Progress lost on a failed save**: the record was dropped instead of retried
- **Stale data on quick reconnect**: reconnecting before the last save landed loaded the old row
- **Weapon and hit stats overwritten** after a failed read, which was treated as "no data"
- **Players connecting before the database was ready** stayed untracked for the whole session
- **`!resetmyrank` racing the save**: a save in flight could undo the reset
- `setpoints` and the API's `SetPlayerPoints` now update the stored rank id along with the points

## [v1.3.1]

### Fixed

- **Missing `lvl_base_settings` table**: `Table 'x.lvl_base_settings' doesn't exist` on every player connect
  - The core migration bailed out as soon as it saw an existing `lvl_base`, so it never created the settings table or the `lvl_base` indexes - yet was still recorded as applied, leaving them permanently missing
  - Hit anyone pointing the plugin at an existing LVL Ranks database (the documented upgrade path), and anyone whose first migration run failed after `lvl_base` was created
  - Each object is now checked for existence on its own, and a repair migration creates whatever is missing on databases that already recorded the old one
- **Database users without DDL permissions**: a failed migration no longer disables the plugin outright
  - Shared hosting commonly grants `SELECT`/`INSERT`/`UPDATE`/`DELETE` but not `CREATE`/`ALTER`/`INDEX`, which made every startup fail with a bare "Failed to initialize database" even when the tables existed and were perfectly usable
  - Startup now verifies the tables it actually needs (honouring the module toggles) and keeps running when they are all usable, logging a warning that future schema changes will not be applied
  - When a required table really is missing, the error names it and says which permissions to grant instead of just dumping a stack trace

## [v1.3.0]

### Added

- **Table prefix** ([#2](https://github.com/shmitzas/K4-LevelRanks-Upkeep/issues/2)): new `Database.TablePrefix` config option, empty by default
  - Prefixes every table the plugin owns (`awp_` gives `awp_lvl_base`, `awp_lvl_base_settings`, ...), so several game modes can share one database instead of needing a database each
  - Each prefix gets its own `VersionInfo` migration ledger, index and constraint names, so prefixed servers create and upgrade their schema independently
  - Rejected at startup if it contains anything other than letters, digits and underscores
  - Empty prefix keeps the stock LVL Ranks names, so existing installs are unaffected

## [v1.1.1]

### Fixed

- **Critical issue fix**: Fixed `ObjectDisposedException` when players disconnect
  - Migrated from game events (`EventPlayerActivate`, `EventPlayerDisconnect`) to SwiftlyS2 API events
  - Now using `Core.Event.OnClientPutInServer` for player connect handling
  - Now using `Core.Event.OnClientDisconnected` for player disconnect handling
  - API events fire before player object disposal, preventing unobserved task exceptions

## [v1.1.0]

### Added

- **Playtime commands** ([#3](https://github.com/K4ryuu/K4-LevelRanks-SwiftlyS2/issues/3)):
  - `!mytime` (aliases: `playtime`, `servertime`) - Shows player's total time on server
  - `!ttop` (aliases: `timetop`, `toptime`) - Shows top players by playtime leaderboard
  - Playtime formatting with localizable units (days, hours, minutes)
- **Position notifications** (requested by Mafel): Top list commands now display player's rank in chat
  - `!top` shows: "You are ranked #X out of Y players"
  - `!ttop` shows: "You are ranked #X out of Y players by playtime"

### Changed

- **Top players menu**: Now displays rank tag alongside player name and points
  - Format: `#1 PlayerName [GN1] - 5000 pts` (using short rank tag instead of full name)

### Fixed

- **Race condition fix** ([#5](https://github.com/K4ryuu/K4-LevelRanks-SwiftlyS2/issues/5)): Fixed duplicate entry MySQL error when saving player data concurrently
  - Changed from check-then-act pattern to try-catch INSERT with UPDATE fallback
  - Applied fix to all save operations: PlayerData, PlayerSettings, WeaponStats, HitData
- **Security fix** ([#6](https://github.com/K4ryuu/K4-LevelRanks-SwiftlyS2/issues/6)): Added permission enforcement for admin commands
  - Admin commands now require `k4-levelranks.admin` permission
  - Affected commands: `setpoints`, `givepoints`, `removepoints`

## [v1.0.3]

### Changed

- **GameRules handling**: Updated to use `Core.EntitySystem.GetGameRules()` API directly instead of querying entities
- **Config system**: Migrated from `IOptions<T>` to `IOptionsMonitor<T>` for reactive config updates
  - All config values now accessed via `.CurrentValue` property
  - Configs can be reloaded at runtime without server restart
- **Config registration**: Simplified config initialization to use `AddOptions<T>()` with `BindConfiguration()`

### Technical

- Refactored Plugin.cs to use simplified GameRules accessor
- Updated all config accesses across Events, Commands, Services to use `CurrentValue` pattern
- Configuration now supports live-reload via SwiftlyS2's `reloadOnChange: true` setting

## [v1.0.2]

### Fixed

- Fixed new players not being saved to database (INSERT missing primary key due to Dommel assuming auto-generated keys)
- Added `[DatabaseGenerated(DatabaseGeneratedOption.None)]` attribute to all model primary keys (PlayerData, PlayerSettings, WeaponStatRecord, HitData)

## [v1.0.1]

### Added

- **Multi-database support**: Now supports MySQL/MariaDB, PostgreSQL, and SQLite
- **Database migrations**: Automatic schema management with FluentMigrator
- **ORM integration**: Dapper + Dommel for type-safe database operations

### Changed

- Refactored database layer to use Dommel ORM instead of raw SQL queries
- Improved database compatibility across different database engines
- Optimized publish output by excluding unused language resources and database providers

### Fixed

- Fixed SQL syntax compatibility issues with different MySQL/MariaDB versions
