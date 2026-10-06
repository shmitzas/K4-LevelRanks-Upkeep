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

		/// <summary>Empty when the module is off. Throws on database errors.</summary>
		private async Task<List<WeaponStatRecord>> LoadWeaponStatsAsync(IDbConnection connection, string steamId)
		{
			if (!_modules.WeaponStatsEnabled)
				return [];

			var result = await connection.SelectAsync<WeaponStatRecord>(w => w.Steam == steamId);
			return [.. result];
		}

		// =========================================
		// =           SAVE OPERATIONS
		// =========================================

		private Task DeleteWeaponStatsAsync(IDbConnection connection, IDbTransaction transaction, string steamId, CancellationToken ct)
			=> _modules.WeaponStatsEnabled
				? connection.DeleteMultipleAsync<WeaponStatRecord>(w => w.Steam == steamId, transaction, ct)
				: Task.CompletedTask;
	}
}
