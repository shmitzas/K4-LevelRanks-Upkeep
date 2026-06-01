# K4-LevelRanks Shared API

The Shared API lets external SwiftlyS2 plugins read and manipulate player data managed by K4-LevelRanks at runtime without direct assembly coupling.

---

## Setup

### 1. Reference the shared assembly

Add `K4-LevelRanksSharedApi.dll` to your plugin's project. Do **not** ship it with your plugin — it is provided by K4-LevelRanks at runtime.

```xml
<ItemGroup>
  <Reference Include="K4-LevelRanksSharedApi">
    <HintPath>path/to/K4-LevelRanksSharedApi.dll</HintPath>
    <ExcludeAssets>runtime</ExcludeAssets>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

### 2. Obtain the API instance

The API is registered under the version key `"K4LevelRanks.Api.v1"`. Resolve it through SwiftlyS2's `IInterfaceManager` after both plugins are loaded.

```csharp
using K4RanksSharedApi;
using SwiftlyS2.Shared.Plugins;

public sealed class MyPlugin(ISwiftlyCore core) : BasePlugin(core)
{
    private IK4RanksApi? _k4Api;

    public override void Load(bool hotReload)
    {
        _k4Api = Core.InterfaceManager
            .GetSharedInterface<IK4RanksApi>("K4LevelRanks.Api.v1");
    }
}
```

> Always null-check the resolved instance. If K4-LevelRanks is not loaded the call returns `null`.

---

## Interface reference — `IK4RanksApi`

**Namespace:** `K4RanksSharedApi`  
**Assembly:** `K4-LevelRanksSharedApi.dll`

---

### Player Data

#### `IsPlayerDataLoaded`

```csharp
bool IsPlayerDataLoaded(IPlayer player)
```

Returns `true` when the player's data has been loaded from the database and is ready to read or modify. Always check this before calling any other API method for a player.

| Parameter | Type | Description |
|-----------|------|-------------|
| `player` | `IPlayer` | The target player. |

**Returns:** `true` if data is loaded; `false` if the player is unknown or data is still being fetched.

---

#### `GetPlayerStat`

```csharp
object? GetPlayerStat(IPlayer player, string field)
```

Returns a single statistic value by field name. Field lookup is **case-insensitive**. Returns `null` when the player's data is not loaded or the field name is not recognised.

| Parameter | Type | Description |
|-----------|------|-------------|
| `player` | `IPlayer` | The target player. |
| `field` | `string` | Field name — see tables below. |

##### Standard fields

| Field | Return type | Description |
|-------|-------------|-------------|
| `value` / `points` | `int` | Current points (XP). |
| `name` | `string` | Player display name. |
| `steam` | `string` | Steam ID (`STEAM_X:X:XXXXXX`). |
| `kills` | `int` | Total kills. |
| `deaths` | `int` | Total deaths. |
| `assists` | `int` | Total assists. |
| `headshots` | `int` | Total headshot kills. |
| `shoots` | `long` | Total shots fired. |
| `hits` | `long` | Total shots that hit a player. |
| `damage` | `long` | Total damage dealt. |
| `round_win` | `int` | Rounds won. |
| `round_lose` | `int` | Rounds lost. |
| `rounds_played` | `int` | Total rounds played. |
| `game_wins` | `int` | Full match wins. |
| `game_losses` | `int` | Full match losses. |
| `games_played` | `int` | Total matches played. |
| `playtime` | `long` | Total playtime in seconds. |

##### Computed fields

| Field | Return type | Description |
|-------|-------------|-------------|
| `kdr` | `double` | Kill/death ratio (rounded to 2 dp; `kills` when `deaths == 0`). |
| `accuracy` | `double` | `hits / shoots × 100` (rounded to 1 dp). |
| `hspercent` | `double` | `headshots / kills × 100` (rounded to 1 dp). |

##### Rank fields

| Field | Return type | Description |
|-------|-------------|-------------|
| `rankid` | `int` | 1-based index in the configured ranks list. |
| `rankname` | `string` | Display name of the player's current rank. |
| `ranktag` | `string` | Short tag used in scoreboard clan tags. |
| `rankcolor` | `string` | Chat colour code (e.g. `[gold]`, `[red]`). |

##### Weapon stat fields

Use the format `weapon.<classname>.<stat>` where `<classname>` is the CS2 weapon classname **without** the `weapon_` prefix.

| `<stat>` | Return type | Description |
|----------|-------------|-------------|
| `kills` | `int` | Kills with this weapon. |
| `deaths` | `int` | Deaths while holding this weapon. |
| `headshots` | `int` | Headshot kills with this weapon. |
| `hits` | `long` | Hits landed with this weapon. |
| `shots` | `long` | Shots fired with this weapon. |
| `damage` | `long` | Damage dealt with this weapon. |

```csharp
// Examples
var points  = (int)   _k4Api.GetPlayerStat(player, "points")!;
var kdr     = (double)_k4Api.GetPlayerStat(player, "kdr")!;
var akKills = (int)   _k4Api.GetPlayerStat(player, "weapon.ak47.kills")!;
var rank    = (string)_k4Api.GetPlayerStat(player, "rankname")!;
```

---

### Points Management

#### `ModifyPlayerPoints`

```csharp
void ModifyPlayerPoints(IPlayer player, int amount, string reason, bool showMessage = true)
```

Adds or subtracts points from a player. Triggers rank-up/rank-down logic, scoreboard clan tag update, and (optionally) a chat notification to the player.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `player` | `IPlayer` | — | The target player. |
| `amount` | `int` | — | Points to add (positive) or subtract (negative). |
| `reason` | `string` | — | Translation key or plain text shown in the chat message as the reason. |
| `showMessage` | `bool` | `true` | When `false`, no chat message is sent to the player. |

```csharp
// Award 50 points with a chat message
_k4Api.ModifyPlayerPoints(player, 50, "k4.reason.bonus");

// Deduct 100 silently
_k4Api.ModifyPlayerPoints(player, -100, "k4.reason.penalty", showMessage: false);
```

---

#### `SetPlayerPoints`

```csharp
void SetPlayerPoints(IPlayer player, int points)
```

Sets the player's points to an exact value. Bypasses rank-change chat notifications but updates the clan tag when the `Clantags` scoreboard option is enabled. Marks the player's data as dirty so the change is persisted on the next save.

| Parameter | Type | Description |
|-----------|------|-------------|
| `player` | `IPlayer` | The target player. |
| `points` | `int` | Exact point value to assign. |

```csharp
// Reset a player to 0 points
_k4Api.SetPlayerPoints(player, 0);
```

> Use `ModifyPlayerPoints` when you want rank-change messages and proper rank transitions to fire. Use `SetPlayerPoints` for administrative overrides where silent assignment is preferred.

---

### Rankings

Both methods are asynchronous because they query the database.

#### `GetPlayerPositionAsync`

```csharp
Task<int> GetPlayerPositionAsync(IPlayer player)
```

Returns the player's leaderboard position (1 = highest points). Returns `-1` if the player has no database record.

```csharp
int position = await _k4Api.GetPlayerPositionAsync(player);
int total    = await _k4Api.GetTotalPlayersAsync();
player.SendChat($"You are rank #{position} out of {total} players.");
```

---

#### `GetTotalPlayersAsync`

```csharp
Task<int> GetTotalPlayersAsync()
```

Returns the total number of players stored in the database, regardless of whether they are currently online.

---

### Scoreboard Rank Display

K4-LevelRanks updates every player's CS2 competitive rank icon on `round_prestart`. The methods below let you override what rank icon is shown for a player without modifying their actual points or triggering rank-up logic.

#### `UpdatePlayerScoreboard`

```csharp
void UpdatePlayerScoreboard(IPlayer player)
```

Forces an immediate scoreboard refresh for a single player, applying their real rank or any active virtual override. Useful after manually changing something that affects display before the next `round_prestart`.

---

#### `SetScoreboardRankOverride`

```csharp
void SetScoreboardRankOverride(IPlayer player, int points)
```

Overrides the rank icon and point value shown for a player in the CS2 scoreboard. Both the rank icon (all scoreboard modes) and the raw number shown in Premier mode are derived from `points`. The player's actual points, stats, and database record are completely unaffected. The override is session-only — it is never persisted and is cleared when the player disconnects.

Calls `UpdatePlayerScoreboard` internally so the change is visible immediately.

| Parameter | Type | Description |
|-----------|------|-------------|
| `player` | `IPlayer` | The target player. |
| `points` | `int` | Virtual point value to display. The rank icon is computed from this value using the same rank thresholds as real points. |

```csharp
// Show the rank and point count for 5000 virtual points
_k4Api.SetScoreboardRankOverride(player, 5000);
```

---

#### `ClearScoreboardRankOverride`

```csharp
void ClearScoreboardRankOverride(IPlayer player)
```

Removes a previously set virtual rank override. The scoreboard reverts to displaying the rank computed from the player's real points on the next update. Calls `UpdatePlayerScoreboard` immediately so the revert is visible at once.

```csharp
_k4Api.ClearScoreboardRankOverride(player);
```

---

#### `GetScoreboardRankOverride`

```csharp
int? GetScoreboardRankOverride(IPlayer player)
```

Returns the currently active virtual point override for the player, or `null` if no override is set and the real rank is being displayed.

```csharp
int? virtualPoints = _k4Api.GetScoreboardRankOverride(player);
if (virtualPoints.HasValue)
    // Scoreboard is showing rank derived from virtualPoints.Value
    // Real points: (int)_k4Api.GetPlayerStat(player, "points")!
```

---

## Displaying a virtual rank without modifying real points

A common pattern in consumer plugins is showing a different rank icon in the scoreboard temporarily—for example during a match override, a trial period, or a cosmetic feature—while the player's real stats and points remain unchanged.

### The wrong approach

```csharp
// ❌ DANGEROUS — do not do this
K4RanksApi.SetPlayerPoints(player, virtualPoints);
K4RanksApi.UpdatePlayerScoreboard(player);
K4RanksApi.SetPlayerPoints(player, livePoints);
```

This pattern has two critical problems:

1. **Database corruption risk.** `SetPlayerPoints` sets `IsDirty = true` both times. If the plugin's periodic save fires between the two calls, the intermediate virtual value gets written to the database and the player permanently loses their real points.
2. **Unnecessary dirty flag.** Even when no save occurs between the calls, the player's record is marked dirty and will be written to the database on the next periodic interval, stamping the restored value as if it had genuinely changed.

### The correct approach

Use `SetScoreboardRankOverride`. It operates entirely on the scoreboard display layer — `PlayerData`, `Points`, and `IsDirty` are never touched.

```csharp
// ✅ Correct — real points are completely untouched

// Display the rank and Premier-mode score for 5000 virtual points
_k4Api.SetScoreboardRankOverride(player, 5000);

// Revert to the rank computed from the player's real points
_k4Api.ClearScoreboardRankOverride(player);
```

The override is applied immediately and is replayed on every game tick so the icon stays visible. It is automatically cleared when the player disconnects, so no cleanup is needed on your side in the common case.

### Anatomy of the override system

| Layer | Modified by override? | Persisted to DB? |
|---|---|---|
| `CompetitiveRanking` / `CompetitiveRankType` (controller fields) | ✅ Yes — display only | No |
| `PlayerData.Points` | ❌ Never | — |
| `PlayerData.IsDirty` | ❌ Never | — |
| Rank-up / rank-down chat messages | ❌ Never fired | — |
| Clan tag | ❌ Not updated by override | — |

### Checking for an active override

```csharp
int? virtualPoints = _k4Api.GetScoreboardRankOverride(player);
if (virtualPoints.HasValue)
{
    // Scoreboard is showing the rank and score for virtualPoints.Value
    // Real rank is still: (int)_k4Api.GetPlayerStat(player, "rankid")!
}
```

---

## Notes

- All API methods that accept `IPlayer` are no-ops (or return `null` / `false`) when the player's data is not yet loaded. Always guard with `IsPlayerDataLoaded` where necessary.
- Points modifications made through the API (`ModifyPlayerPoints`, `SetPlayerPoints`) are written to the database on the next periodic save or when the player disconnects — they are not written immediately.
- Scoreboard rank overrides survive config reloads but are cleared on plugin unload or player disconnect.
