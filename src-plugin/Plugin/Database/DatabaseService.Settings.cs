using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using Dommel;

namespace K4Ranks;

public sealed partial class Plugin
{
	public sealed partial class DatabaseService
	{
		// =========================================
		// =           LOAD OPERATIONS
		// =========================================

		/// <summary>Null when the player has no settings row. Throws on database errors.</summary>
		private static Task<PlayerSettings?> LoadSettingsAsync(IDbConnection connection, string steamId)
			=> connection.GetAsync<PlayerSettings>(steamId);
	}
}

// =========================================
// =           PLAYER SETTINGS MODEL
// =========================================

/// <summary>
/// Player settings model - separate from stats
/// </summary>
[Table("lvl_base_settings")]
public sealed class PlayerSettings
{
	[Key]
	[DatabaseGenerated(DatabaseGeneratedOption.None)]
	[Column("steam")]
	public string Steam { get; set; } = "";

	[Column("messages")]
	public bool Messages { get; set; } = true;

	[Column("summary")]
	public bool Summary { get; set; } = false;

	[Column("rankchanges")]
	public bool RankChanges { get; set; } = true;

	// A version, not a dirty flag: menu clicks can run off the game thread, so a flag cleared
	// by a save could swallow a change made while that save was reading.
	private int _version;
	private int _syncedVersion;
	private int _sentVersion;

	internal void Touch() => Interlocked.Increment(ref _version);

	internal void MarkSynced() => _syncedVersion = Volatile.Read(ref _version);

	internal bool HasPendingChanges() => Volatile.Read(ref _version) != _syncedVersion;

	/// <summary>The row when changed since the last committed save; otherwise null.</summary>
	internal PlayerSettings? BuildChange(string steam)
	{
		_sentVersion = Volatile.Read(ref _version);
		return _sentVersion == _syncedVersion
			? null
			: new PlayerSettings { Steam = steam, Messages = Messages, Summary = Summary, RankChanges = RankChanges };
	}

	internal void AcknowledgeSync() => _syncedVersion = _sentVersion;
}
