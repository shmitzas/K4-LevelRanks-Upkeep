using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Dommel;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Ranks;

// =========================================
// =           WEAPON STAT MODEL
// =========================================

/// <summary>
/// Per-weapon statistics for a player
/// </summary>
public sealed class WeaponStat
{
	public required string WeaponClassname { get; init; }
	public CSWeaponType WeaponType { get; set; }

	public int Kills { get; set; }
	public int Deaths { get; set; }
	public int Headshots { get; set; }
	public long Hits { get; set; }
	public long Shots { get; set; }
	public long Damage { get; set; }

	// Values the last committed save wrote; the stat is written again whenever it differs.
	internal WeaponCounters Synced;

	internal WeaponCounters ReadCounters() => new()
	{
		Kills = Kills,
		Deaths = Deaths,
		Headshots = Headshots,
		Hits = Hits,
		Shots = Shots,
		Damage = Damage
	};
}

// =========================================
// =           WEAPON STATS COLLECTION
// =========================================

/// <summary>
/// Collection of weapon stats for a player (runtime). Written on the game thread and
/// snapshotted by the background save, so every access to the dictionary is locked.
/// </summary>
public sealed class PlayerWeaponStats
{
	private readonly Dictionary<string, WeaponStat> _stats = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _lock = new();

	// Stats the save in flight carries, with the values written.
	private List<(WeaponStat Stat, WeaponCounters Sent)>? _sent;

	// =========================================
	// =           ACCESSORS
	// =========================================

	/// <summary>Get or create weapon stat entry</summary>
	public WeaponStat GetOrCreate(string weaponClassname)
	{
		var key = NormalizeWeaponName(weaponClassname);

		lock (_lock)
		{
			if (!_stats.TryGetValue(key, out var stat))
			{
				var weaponInfo = Plugin.WeaponCache.GetByClassname(key);
				stat = new WeaponStat
				{
					WeaponClassname = key,
					WeaponType = weaponInfo?.Type ?? CSWeaponType.WEAPONTYPE_UNKNOWN
				};
				_stats[key] = stat;
			}

			return stat;
		}
	}

	/// <summary>Get weapon stat if exists</summary>
	public WeaponStat? Get(string weaponClassname)
	{
		var key = NormalizeWeaponName(weaponClassname);

		lock (_lock)
			return _stats.TryGetValue(key, out var stat) ? stat : null;
	}

	/// <summary>Snapshot of all weapon stats</summary>
	public IReadOnlyCollection<WeaponStat> GetAll()
	{
		lock (_lock)
			return [.. _stats.Values];
	}

	// =========================================
	// =           SAVE STATE
	// =========================================

	/// <summary>Rows for weapons changed since the last committed save; null when none did.</summary>
	internal List<WeaponStatRecord>? BuildChanges(string steam)
	{
		lock (_lock)
		{
			List<WeaponStatRecord>? rows = null;
			_sent = null;

			foreach (var stat in _stats.Values)
			{
				var current = stat.ReadCounters();
				if (current == stat.Synced)
					continue;

				(_sent ??= []).Add((stat, current));
				(rows ??= []).Add(new WeaponStatRecord
				{
					Steam = steam,
					Classname = stat.WeaponClassname,
					Kills = current.Kills,
					Deaths = current.Deaths,
					Headshots = current.Headshots,
					Hits = current.Hits,
					Shots = current.Shots,
					Damage = current.Damage
				});
			}

			return rows;
		}
	}

	internal void AcknowledgeSync()
	{
		lock (_lock)
		{
			if (_sent is null)
				return;

			foreach (var (stat, sent) in _sent)
				stat.Synced = sent;

			_sent = null;
		}
	}

	internal bool HasPendingChanges()
	{
		lock (_lock)
		{
			foreach (var stat in _stats.Values)
			{
				if (stat.ReadCounters() != stat.Synced)
					return true;
			}

			return false;
		}
	}

	internal void MarkSynced()
	{
		lock (_lock)
		{
			foreach (var stat in _stats.Values)
				stat.Synced = stat.ReadCounters();
		}
	}

	// =========================================
	// =           MANAGEMENT
	// =========================================

	/// <summary>Clear all stats</summary>
	public void Clear()
	{
		lock (_lock)
		{
			_stats.Clear();
			_sent = null;
		}
	}

	/// <summary>Load from database records</summary>
	public void LoadFrom(IEnumerable<WeaponStatRecord> records)
	{
		lock (_lock)
		{
			_stats.Clear();

			foreach (var record in records)
			{
				var weaponInfo = Plugin.WeaponCache.GetByClassname(record.Classname);
				var stat = new WeaponStat
				{
					WeaponClassname = record.Classname,
					WeaponType = weaponInfo?.Type ?? CSWeaponType.WEAPONTYPE_UNKNOWN,
					Kills = record.Kills,
					Deaths = record.Deaths,
					Headshots = record.Headshots,
					Hits = record.Hits,
					Shots = record.Shots,
					Damage = record.Damage
				};

				_stats[record.Classname] = stat;
			}
		}
	}

	// =========================================
	// =           HELPERS
	// =========================================

	private static string NormalizeWeaponName(string name)
	{
		var normalized = name.ToLowerInvariant();
		return normalized.StartsWith("weapon_") ? normalized : $"weapon_{normalized}";
	}
}

// =========================================
// =           DATABASE RECORD
// =========================================

/// <summary>
/// Database record for weapon stats - LVL Ranks ExStats Weapons compatible
/// </summary>
[Table("lvl_base_weapons")]
public sealed class WeaponStatRecord
{
	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.None)]
	[Column("steam")]
	public string Steam { get; set; } = "";

	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.None)]
	[Column("classname")]
	public string Classname { get; set; } = "";

	[Column("kills")]
	public int Kills { get; set; }

	[Column("deaths")]
	public int Deaths { get; set; }

	[Column("headshots")]
	public int Headshots { get; set; }

	[Column("hits")]
	public long Hits { get; set; }

	[Column("shots")]
	public long Shots { get; set; }

	[Column("damage")]
	public long Damage { get; set; }
}
