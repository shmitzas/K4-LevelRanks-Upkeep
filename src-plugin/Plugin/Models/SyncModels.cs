namespace K4Ranks;

// Each record keeps the values its last committed save wrote; a part is written again
// whenever its current values differ from those.

public record struct RankCounters
{
	public int Value { get; set; }
	public int Kills { get; set; }
	public int Deaths { get; set; }
	public long Shoots { get; set; }
	public long Hits { get; set; }
	public int Headshots { get; set; }
	public int Assists { get; set; }
	public int RoundWin { get; set; }
	public int RoundLose { get; set; }
	public long Playtime { get; set; }
	public int GameWins { get; set; }
	public int GameLosses { get; set; }
	public int GamesPlayed { get; set; }
	public int RoundsPlayed { get; set; }
	public long Damage { get; set; }
}

public record struct WeaponCounters
{
	public int Kills { get; set; }
	public int Deaths { get; set; }
	public int Headshots { get; set; }
	public long Hits { get; set; }
	public long Shots { get; set; }
	public long Damage { get; set; }
}

public record struct HitCounters
{
	public long DmgHealth { get; set; }
	public long DmgArmor { get; set; }
	public int Head { get; set; }
	public int Chest { get; set; }
	public int Belly { get; set; }
	public int LeftArm { get; set; }
	public int RightArm { get; set; }
	public int LeftLeg { get; set; }
	public int RightLeg { get; set; }
	public int Neak { get; set; }
}

/// <summary>
/// One player's absolute values for a single save transaction. A null part is unchanged and
/// not written; <see cref="Reset"/> deletes the weapon and hit rows before anything is written.
/// </summary>
public sealed class PlayerSaveEntry
{
	public required string Steam { get; init; }
	public bool Reset { get; init; }
	public PlayerData? Base { get; init; }
	public PlayerSettings? Settings { get; init; }
	public List<WeaponStatRecord>? Weapons { get; init; }
	public HitData? Hits { get; init; }
}

/// <summary>A player's four tables as loaded; <see cref="Base"/> is null for a new player.</summary>
public sealed record PlayerRecord(PlayerData? Base, PlayerSettings? Settings, List<WeaponStatRecord> Weapons, HitData? Hits);
