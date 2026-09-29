<div align="center">
  <h1 align="center">KitsuneLab©</h1>
  <h3 align="center">K4-LevelRanks</h3>
  <a align="center">A comprehensive ranking and statistics system for Counter-Strike 2. Features point-based progression, detailed player statistics, weapon tracking, and LVL Ranks database compatibility.</a>
</div>

<p align="center">
  <img src="https://img.shields.io/badge/build-passing-brightgreen" alt="Build Status">
  <img src="https://img.shields.io/github/downloads/Shmitzas/K4-LevelRanks-Upkeep/total?style=flat&logo=github&cacheSeconds=3600" alt="Downloads">
  <img src="https://img.shields.io/github/stars/Shmitzas/K4-LevelRanks-Upkeep?style=flat&logo=github&cacheSeconds=3600" alt="Stars">
  <img src="https://img.shields.io/github/license/Shmitzas/K4-LevelRanks-Upkeep" alt="License">
</p>

# Important notice!
> [!IMPORTANT]  
> [K4ryuu](https://github.com/K4ryuu) is the creator of this plugin.<br>
> Since he is no longer maintaining his CS2 plugins, I forked some of them and maintain them ONLY FOR BUG FIXES!

---

## Features

### Ranking System

- **Point-based progression** with fully customizable point values
- **Dynamic point multipliers** based on victim/attacker point ratio
- **Customizable ranks** with colors, tags, and point thresholds
- **LVL Ranks database compatible** - works with existing databases
- **Fake competitive ranks** - Premier, Competitive, Wingman, or custom icon ranks

### Statistics Tracking

- **Combat stats**: Kills, deaths, assists, headshots, K/D ratio, accuracy
- **Round stats**: Wins, losses, rounds played, MVP awards
- **Game stats**: Match wins, losses, games played
- **Playtime tracking** with optional point rewards

### Weapon Statistics (Optional Module)

- Per-weapon kills, deaths, headshots
- Shots fired, hits, accuracy per weapon
- Damage dealt per weapon

### Hit Statistics (Optional Module)

- Hitbox/body part tracking (ExStats Hits compatible)
- Damage distribution by body region
- Head, chest, stomach, arms, legs tracking

### Point Events

| Category          | Events                                                                |
| ----------------- | --------------------------------------------------------------------- |
| **Combat**        | Kill, Death, Headshot, Assist, Flash Assist, Team Kill, Suicide       |
| **Special Kills** | No-scope, Through Smoke, Blind Kill, Wallbang, Long Distance          |
| **Weapon Kills**  | Knife, Taser, Grenade, Molotov/Incendiary, Impact (Flash/Smoke/Decoy) |
| **Killstreaks**   | Double Kill → God Like (12 levels)                                    |
| **Objectives**    | Bomb Plant/Defuse/Explode, Hostage Rescue/Hurt/Kill                   |
| **Round**         | Round Win/Lose, MVP                                                   |
| **Playtime**      | Configurable points per X minutes                                     |

### Scoreboard Integration

- **Clan tag ranks** - Show rank in player's clan tag
- **Score sync** - Sync scoreboard score with points
- **Competitive rank display** - Premier, Competitive, Wingman, or custom ranks

### VIP Support

- Point multiplier for VIP players
- Configurable permission flags

### Developer API

- Shared API for other plugins (`K4LevelRanks.Api.v1`)
- Access player data, points, ranks programmatically

---

## Dependencies

- [**SwiftlyS2**](https://github.com/swiftly-solution/swiftlys2): Server plugin framework for Counter-Strike 2
- **Database**: One of the following supported databases:
  - **MySQL / MariaDB** - Recommended for production
  - **PostgreSQL** - Full support
  - **SQLite** - Great for single-server setups

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

## Installation

1. Install [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2) on your server
2. Configure your database connection in SwiftlyS2's `database.jsonc` (MySQL, PostgreSQL, or SQLite)
3. [Download the latest release](https://github.com/shmitzas/K4-Arenas-Upkeep/releases/latest)
4. Extract to your server's `swiftlys2/plugins/` directory
5. Configure the plugin files in `swiftlys2/configs/plugins/k4.levelranks/`
6. Restart your server - database tables will be created automatically

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

## Configuration Files

### `config.json` - Main Settings

| Section        | Option               | Description                                 | Default                 |
| -------------- | -------------------- | ------------------------------------------- | ----------------------- |
| **Database**   | `Connection`         | Database connection name                    | `"host"`                |
|                | `TablePrefix`        | Prefix for this server's tables (see below) | `""`                    |
|                | `PurgeDays`          | Days to keep inactive records (0 = forever) | `30`                    |
| **Rank**       | `StartPoints`        | Starting points for new players             | `0`                     |
|                | `MinPlayers`         | Minimum players for points to be awarded    | `4`                     |
|                | `WarmupPoints`       | Allow points during warmup                  | `false`                 |
|                | `PointsForBots`      | Award points for killing bots               | `false`                 |
|                | `FFAMode`            | FFA mode (no team penalties)                | `false`                 |
| **Scoreboard** | `Clantags`           | Show rank in clan tags                      | `true`                  |
|                | `ScoreSync`          | Sync score with points                      | `false`                 |
|                | `UseRanks`           | Show competitive ranks                      | `true`                  |
|                | `RankMode`           | 1=Premier, 2=Competitive, 3=Wingman, 4=DZ   | `1`                     |
| **Points**     | `RoundEndSummary`    | Show summary instead of per-action messages | `false`                 |
|                | `DynamicDeathPoints` | Dynamic multiplier based on point ratio     | `true`                  |
|                | `ShowPlayerNames`    | Show player names in point messages         | `false`                 |
| **VIP**        | `Multiplier`         | Point multiplier for VIP                    | `1.25`                  |
|                | `Flags`              | Permission flags for VIP status             | `["k4-levelranks.vip"]` |

### `points.json` - Point Values

Fully customizable point values for all events. Set to `0` to disable any event.

### `ranks.json` - Rank Configuration

```json
{
  "Ranks": [
    { "Name": "Silver I", "Tag": "[S1]", "Color": "GRAY", "Points": 0 },
    { "Name": "Gold Nova I", "Tag": "[GN1]", "Color": "GOLD", "Points": 1000 },
    { "Name": "Global Elite", "Tag": "[GE]", "Color": "YELLOW", "Points": 5000 }
  ]
}
```

### `modules.json` - Optional Features

| Module               | Description                       | Default |
| -------------------- | --------------------------------- | ------- |
| `WeaponStatsEnabled` | Track per-weapon statistics       | `true`  |
| `HitStatsEnabled`    | Track hitbox/body part statistics | `true`  |

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

## Commands

### Player Commands

| Command           | Aliases                | Description                   |
| ----------------- | ---------------------- | ----------------------------- |
| `!rank`           | `!myrank`              | Open main rank menu           |
| `!ranks`          | `!ranklist`            | View all available ranks      |
| `!top`            | `!ranktop`, `!toplist` | View top players              |
| `!stats`          | `!mystats`, `!stat`    | View detailed statistics      |
| `!weaponstats`    | `!ws`                  | View weapon statistics        |
| `!hitstats`       | `!hs`                  | View hit/body part statistics |
| `!settings`       | `!options`             | Player settings menu          |
| `!resetmyrank`    | -                      | Reset your own rank           |
| `!togglepointmsg` | -                      | Toggle point messages         |

### Admin Commands

| Command                           | Permission            | Description               |
| --------------------------------- | --------------------- | ------------------------- |
| `!setpoints <target> <amount>`    | `k4-levelranks.admin` | Set player's points       |
| `!givepoints <target> <amount>`   | `k4-levelranks.admin` | Give points to player     |
| `!removepoints <target> <amount>` | `k4-levelranks.admin` | Remove points from player |

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

## Database Structure

The plugin uses LVL Ranks compatible database tables:

- `lvl_base` - Main player statistics
- `lvl_base_settings` - Player preferences
- `lvl_base_weapons` - Weapon statistics (optional)
- `lvl_base_hits` - Hit statistics (optional)

### Separate Ranks per Server (`TablePrefix`)

By default `TablePrefix` is empty and the table names above are used as-is.

Set it to give a server its own set of tables so several game modes can share one
database instead of needing a database each:

```json
{
  "Database": {
    "Connection": "host",
    "TablePrefix": "awp_"
  }
}
```

| `TablePrefix` | Tables used                                                                  |
| ------------- | ---------------------------------------------------------------------------- |
| `""`          | `lvl_base`, `lvl_base_settings`, `lvl_base_weapons`, `lvl_base_hits`         |
| `"awp_"`      | `awp_lvl_base`, `awp_lvl_base_settings`, `awp_lvl_base_weapons`, `awp_lvl_base_hits` |

- The prefix is used **literally** - include the separator yourself (`"awp_"`, not `"awp"`).
- Only letters, digits and underscores are allowed; anything else is rejected at startup.
- Each prefix gets its own `VersionInfo` table, so prefixed servers create and upgrade
  their schema independently of each other.
- Changing the prefix points the server at a different (possibly empty) set of tables -
  it does not move existing data. Rename the tables yourself if you want to keep it.
- Takes effect on server restart.

### Supported Databases

| Database        | Status  | Notes                                      |
| --------------- | ------- | ------------------------------------------ |
| MySQL / MariaDB | ✅ Full | Recommended for multi-server setups        |
| PostgreSQL      | ✅ Full | Alternative for existing Postgres setups   |
| SQLite          | ✅ Full | Perfect for single-server, no setup needed |

**Migration from LVL Ranks**: Simply point the plugin to your existing MySQL database - no migration needed!

**Automatic Schema Management**: The plugin uses FluentMigrator to automatically create and update database tables. Optional modules (WeaponStats, HitStats) only create their tables when enabled.

### Database Permissions

Automatic schema management needs `CREATE`, `ALTER` and `INDEX` in addition to the usual
`SELECT`, `INSERT`, `UPDATE` and `DELETE`. Shared hosting often grants only the latter.

Without them the plugin still runs, as long as the tables it needs are already there -
it logs a warning that migrations could not be applied and carries on. Only if a
required table is missing or unreadable does it disable itself, naming the tables in
question so you can either grant the missing rights or create them by hand (the schema
is LVL Ranks compatible, so an existing LVL Ranks database works as-is).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

---

## License

Distributed under the GPL-3.0 License. See [`LICENSE.md`](LICENSE.md) for more information.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
