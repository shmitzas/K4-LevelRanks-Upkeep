# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
