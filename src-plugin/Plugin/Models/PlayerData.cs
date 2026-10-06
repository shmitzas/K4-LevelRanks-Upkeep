using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Dommel;

namespace K4Ranks;

/// <summary>
/// Player data model - compatible with LVL Ranks database structure
/// Table: lvl_base (main stats) + lvl_base_weapons (weapon stats)
/// </summary>
[Table("lvl_base")]
public sealed class PlayerData
{
	// =========================================
	// =    LVL RANKS COMPATIBLE FIELDS
	// =========================================

	/// <summary>SteamID64 in decimal string form (e.g. "76561198XXXXXXXXX"), stored as the primary key in DB</summary>
	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.None)]
	[Column("steam")]
	public string Steam { get; set; } = "";

	/// <summary>Player name</summary>
	[Column("name")]
	public string Name { get; set; } = "";

	/// <summary>Experience/points value</summary>
	[Column("value")]
	public int Value { get; set; }

	/// <summary>Current rank ID</summary>
	[Column("rank")]
	public int Rank { get; set; }

	/// <summary>Total kills</summary>
	[Column("kills")]
	public int Kills { get; set; }

	/// <summary>Total deaths</summary>
	[Column("deaths")]
	public int Deaths { get; set; }

	/// <summary>Total shots fired (global)</summary>
	[Column("shoots")]
	public long Shoots { get; set; }

	/// <summary>Total hits (global)</summary>
	[Column("hits")]
	public long Hits { get; set; }

	/// <summary>Total headshot kills</summary>
	[Column("headshots")]
	public int Headshots { get; set; }

	/// <summary>Total assists</summary>
	[Column("assists")]
	public int Assists { get; set; }

	/// <summary>Rounds won</summary>
	[Column("round_win")]
	public int RoundWin { get; set; }

	/// <summary>Rounds lost</summary>
	[Column("round_lose")]
	public int RoundLose { get; set; }

	/// <summary>Total playtime in seconds</summary>
	[Column("playtime")]
	public long Playtime { get; set; }

	/// <summary>Last connect time (Unix timestamp)</summary>
	[Column("lastconnect")]
	public int LastConnect { get; set; }

	// =========================================
	// =    EXTENDED FIELDS (K4 ADDITIONS)
	// =========================================

	/// <summary>Game/match wins</summary>
	[Column("game_wins")]
	public int GameWins { get; set; }

	/// <summary>Game/match losses</summary>
	[Column("game_losses")]
	public int GameLosses { get; set; }

	/// <summary>Total games played</summary>
	[Column("games_played")]
	public int GamesPlayed { get; set; }

	/// <summary>Total rounds played</summary>
	[Column("rounds_played")]
	public int RoundsPlayed { get; set; }

	/// <summary>Total damage dealt</summary>
	[Column("damage")]
	public long Damage { get; set; }

	// =========================================
	// =    PLAYER SETTINGS
	// =========================================

	/// <summary>Player settings (stored in separate table)</summary>
	[Ignore]
	public PlayerSettings Settings { get; set; } = new();

	// =========================================
	// =    RUNTIME DATA (NOT IN DB)
	// =========================================

	/// <summary>SteamID64 parsed from the <see cref="Steam"/> key; 0 if Steam is not yet set.</summary>
	[Ignore]
	public ulong SteamId64 => ulong.TryParse(Steam, out var id) ? id : 0;
	[Ignore]
	public int RoundPoints { get; set; }
	[Ignore]
	public int Killstreak { get; set; }
	[Ignore]
	public DateTime LastKillTime { get; set; } = DateTime.MinValue;
	[Ignore]
	public DateTime SessionStartTime { get; set; } = DateTime.UtcNow;
	[Ignore]
	public bool IsLoaded { get; set; }

	// =========================================
	// =    SETTINGS ALIASES
	// =========================================

	[Ignore]
	public bool PointMessagesEnabled
	{
		get => Settings.Messages;
		set { Settings.Messages = value; Settings.Touch(); }
	}

	[Ignore]
	public bool ShowRoundSummary
	{
		get => Settings.Summary;
		set { Settings.Summary = value; Settings.Touch(); }
	}

	[Ignore]
	public bool ShowRankChanges
	{
		get => Settings.RankChanges;
		set { Settings.RankChanges = value; Settings.Touch(); }
	}

	// =========================================
	// =    WEAPON & HIT STATS
	// =========================================

	/// <summary>Weapon stats (tracked per weapon)</summary>
	[Ignore]
	public PlayerWeaponStats WeaponStats { get; } = new();

	/// <summary>Hit data (hitbox/body part tracking - ExStats Hits compatible)</summary>
	[Ignore]
	public HitData HitData { get; set; } = new();

	// =========================================
	// =    COMPUTED PROPERTIES
	// =========================================

	[Ignore]
	public double KDR => Deaths == 0 ? Kills : Math.Round((double)Kills / Deaths, 2);
	[Ignore]
	public double HeadshotPercentage => Kills == 0 ? 0 : Math.Round((double)Headshots / Kills * 100, 1);
	[Ignore]
	public double Accuracy => Shoots == 0 ? 0 : Math.Round((double)Hits / Shoots * 100, 1);

	// =========================================
	// =    BACKWARDS COMPATIBILITY ALIASES
	// =========================================

	[Ignore]
	public int Points { get => Value; set => Value = Math.Max(0, value); }
	[Ignore]
	public string PlayerName { get => Name; set => Name = value; }
	[Ignore]
	public int RoundsWon { get => RoundWin; set => RoundWin = value; }
	[Ignore]
	public int RoundsLost { get => RoundLose; set => RoundLose = value; }

	// =========================================
	// =           ROUND METHODS
	// =========================================

	public void ResetRoundData()
	{
		RoundPoints = 0;
		Killstreak = 0;
		LastKillTime = DateTime.MinValue;
	}

	public void ResetKillstreak()
	{
		Killstreak = 0;
		LastKillTime = DateTime.MinValue;
	}

	/// <summary>
	/// Zeroes the record and marks it for a reset write, under the sync lock so a save being
	/// built never mixes pre- and post-reset values.
	/// </summary>
	public void Reset(int startPoints)
	{
		lock (_syncLock)
		{
			Points = startPoints;
			Rank = 0;
			Kills = 0;
			Deaths = 0;
			Assists = 0;
			Headshots = 0;
			Shoots = 0;
			Hits = 0;
			Damage = 0;
			Playtime = 0;
			RoundsWon = 0;
			RoundsLost = 0;
			RoundsPlayed = 0;
			GameWins = 0;
			GameLosses = 0;
			GamesPlayed = 0;
			WeaponStats.Clear();
			HitData.Reset();
			ResetRoundData();
			_resetPending = true;
			_resetGeneration++;
		}
	}

	// =========================================
	// =           PLAYTIME TRACKING
	// =========================================

	/// <summary>Credits whole seconds played since the last call; the remainder carries over.</summary>
	public void UpdatePlaytime()
	{
		var seconds = (long)(DateTime.UtcNow - SessionStartTime).TotalSeconds;
		if (seconds <= 0)
			return;

		Playtime += seconds;
		SessionStartTime = SessionStartTime.AddSeconds(seconds);
	}

	// =========================================
	// =           GLOBAL STAT RECORDING
	// =========================================

	public void RecordGlobalHit(int damage)
	{
		Hits++;
		Damage += damage;
	}

	public void RecordGlobalShot()
	{
		Shoots++;
	}

	// =========================================
	// =           WEAPON STAT RECORDING
	// =========================================

	public void RecordWeaponHit(string weaponClassname, int damage)
	{
		var stat = WeaponStats.GetOrCreate(weaponClassname);
		stat.Hits++;
		stat.Damage += damage;
	}

	public void RecordWeaponShot(string weaponClassname)
	{
		var stat = WeaponStats.GetOrCreate(weaponClassname);
		stat.Shots++;
	}

	public void RecordWeaponKill(string weaponClassname, bool headshot)
	{
		var stat = WeaponStats.GetOrCreate(weaponClassname);
		stat.Kills++;

		if (headshot)
			stat.Headshots++;
	}

	public void RecordWeaponDeath(string weaponClassname)
	{
		var stat = WeaponStats.GetOrCreate(weaponClassname);
		stat.Deaths++;
	}

	// =========================================
	// =           SAVE STATE (runtime only)
	// =========================================

	// Game-thread writes are lock-free; a field changed after the snapshot differs from what
	// was committed and so stays pending.
	private RankCounters _synced;
	private RankCounters _sent;
	private bool _baseRowMissing;
	private bool _resetPending;
	private int _resetGeneration;
	private int _sentResetGeneration;
	private readonly Lock _syncLock = new();

	/// <summary>Adopts the current values as saved; called once a load completes.</summary>
	internal void MarkSynced(bool baseRowMissing)
	{
		lock (_syncLock)
		{
			_synced = ReadCounters();
			_baseRowMissing = baseRowMissing;
			_resetPending = false;
			WeaponStats.MarkSynced();
			HitData.MarkSynced();
			Settings.MarkSynced();
		}
	}

	internal bool HasPendingChanges()
	{
		lock (_syncLock)
		{
			return _resetPending
				|| _baseRowMissing
				|| ReadCounters() != _synced
				|| WeaponStats.HasPendingChanges()
				|| HitData.HasPendingChanges()
				|| Settings.HasPendingChanges();
		}
	}

	/// <summary>
	/// Absolute values of every part changed since the last committed save; null when nothing did.
	/// Nothing counts as saved until <see cref="AcknowledgeSync"/>.
	/// </summary>
	internal PlayerSaveEntry? BuildSaveEntry(int nowUnix)
	{
		lock (_syncLock)
		{
			var stats = ReadCounters();
			var weapons = WeaponStats.BuildChanges(Steam);
			var hits = HitData.BuildChange(Steam);
			var settings = Settings.BuildChange(Steam);
			var baseChanged = _resetPending || _baseRowMissing || stats != _synced;

			_sent = stats;
			_sentResetGeneration = _resetGeneration;

			if (!baseChanged && weapons is null && hits is null && settings is null)
				return null;

			LastConnect = nowUnix;

			return new PlayerSaveEntry
			{
				Steam = Steam,
				Reset = _resetPending,
				Base = baseChanged ? BuildRow(stats, nowUnix) : null,
				Settings = settings,
				Weapons = weapons,
				Hits = hits
			};
		}
	}

	/// <summary>The transaction carrying the entry last built by <see cref="BuildSaveEntry"/> committed.</summary>
	internal void AcknowledgeSync()
	{
		lock (_syncLock)
		{
			_synced = _sent;
			_baseRowMissing = false;

			// A reset made after the entry was built still needs its own write.
			if (_resetGeneration == _sentResetGeneration)
				_resetPending = false;

			WeaponStats.AcknowledgeSync();
			HitData.AcknowledgeSync();
			Settings.AcknowledgeSync();
		}
	}

	private PlayerData BuildRow(RankCounters stats, int nowUnix) => new()
	{
		Steam = Steam,
		Name = Name,
		Rank = Rank,
		LastConnect = nowUnix,
		Value = stats.Value,
		Kills = stats.Kills,
		Deaths = stats.Deaths,
		Shoots = stats.Shoots,
		Hits = stats.Hits,
		Headshots = stats.Headshots,
		Assists = stats.Assists,
		RoundWin = stats.RoundWin,
		RoundLose = stats.RoundLose,
		Playtime = stats.Playtime,
		GameWins = stats.GameWins,
		GameLosses = stats.GameLosses,
		GamesPlayed = stats.GamesPlayed,
		RoundsPlayed = stats.RoundsPlayed,
		Damage = stats.Damage
	};

	private RankCounters ReadCounters() => new()
	{
		Value = Value,
		Kills = Kills,
		Deaths = Deaths,
		Shoots = Shoots,
		Hits = Hits,
		Headshots = Headshots,
		Assists = Assists,
		RoundWin = RoundWin,
		RoundLose = RoundLose,
		Playtime = Playtime,
		GameWins = GameWins,
		GameLosses = GameLosses,
		GamesPlayed = GamesPlayed,
		RoundsPlayed = RoundsPlayed,
		Damage = Damage
	};
}
