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

		/// <summary>Null when the module is off or the player has no row. Throws on database errors.</summary>
		private Task<HitData?> LoadHitDataAsync(IDbConnection connection, string steamId)
			=> _modules.HitStatsEnabled
				? connection.GetAsync<HitData>(steamId)
				: Task.FromResult<HitData?>(null);

		// =========================================
		// =           SAVE OPERATIONS
		// =========================================

		private Task DeleteHitDataAsync(IDbConnection connection, IDbTransaction transaction, string steamId, CancellationToken ct)
			=> _modules.HitStatsEnabled
				? connection.DeleteMultipleAsync<HitData>(h => h.Steam == steamId, transaction, ct)
				: Task.CompletedTask;
	}
}
