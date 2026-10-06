using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;

namespace K4Ranks;

public sealed partial class Plugin
{
	public sealed partial class DatabaseService
	{
		// =========================================
		// =           LOAD OPERATIONS
		// =========================================

		/// <summary>
		/// Loads all four of a player's tables. <see cref="PlayerRecord.Base"/> is null when no row
		/// exists for the SteamID. Throws when any read fails, so callers never build a partial record
		/// whose next absolute save would overwrite the real rows.
		/// </summary>
		public async Task<PlayerRecord> LoadPlayerAsync(string steamId)
		{
			if (!IsEnabled)
				throw new InvalidOperationException("Database is not enabled");

			const int maxAttempts = 3;
			Exception? lastError = null;

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				try
				{
					using var connection = Core.Database.GetConnection(_connectionName);
					connection.Open();

					// Returns null when no row matches the primary key — this is a
					// legitimate "new player" signal and should NOT trigger a retry.
					var player = await connection.GetAsync<PlayerData>(steamId);
					var settings = await LoadSettingsAsync(connection, steamId);
					var weapons = await LoadWeaponStatsAsync(connection, steamId);
					var hits = await LoadHitDataAsync(connection, steamId);

					return new PlayerRecord(player, settings, weapons, hits);
				}
				catch (Exception ex)
				{
					lastError = ex;
					Core.Logger.LogWarning(ex,
						"LoadPlayerAsync attempt {Attempt}/{Max} failed for {Steam}",
						attempt, maxAttempts, steamId);

					if (attempt < maxAttempts)
						await Task.Delay(150 * attempt);
				}
			}

			Core.Logger.LogError(lastError, "Failed to load player {Steam} after {Max} attempts; not tracked until a retry succeeds (within a minute)", steamId, maxAttempts);
			throw lastError!;
		}

		// =========================================
		// =           QUERY OPERATIONS
		// =========================================

		public async Task<int> GetPlayerRankPositionAsync(string steamId)
		{
			if (!IsEnabled)
				return -1;

			try
			{
				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				// Get player's current value
				var player = await connection.GetAsync<PlayerData>(steamId);
				if (player == null)
					return -1;

				// Count players with higher value + 1 (served by idx_value)
				var higherCount = await connection.CountAsync<PlayerData>(p => p.Value > player.Value);
				return (int)higherCount + 1;
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to get rank position for {Steam}", steamId);
				return -1;
			}
		}

		public async Task<int> GetPlayerRankPositionByTimeAsync(string steamId)
		{
			if (!IsEnabled)
				return -1;

			try
			{
				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				// Get player's current playtime
				var player = await connection.GetAsync<PlayerData>(steamId);
				if (player == null)
					return -1;

				// Count players with higher playtime + 1 (served by idx_playtime)
				var higherCount = await connection.CountAsync<PlayerData>(p => p.Playtime > player.Playtime);
				return (int)higherCount + 1;
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to get rank position by time for {Steam}", steamId);
				return -1;
			}
		}

		public async Task<int> GetTotalPlayersAsync()
		{
			if (!IsEnabled)
				return 0;

			try
			{
				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				return (int)await connection.CountAsync<PlayerData>();
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to get total players");
				return 0;
			}
		}

		public async Task<List<PlayerData>> GetTopPlayersAsync(int count = 10)
		{
			if (!IsEnabled)
				return [];

			try
			{
				return await QueryTopPlayersAsync(nameof(K4Ranks.PlayerData.Value), count);
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to get top players");
				return [];
			}
		}

		public async Task<List<PlayerData>> GetTopPlayersByTimeAsync(int count = 10)
		{
			if (!IsEnabled)
				return [];

			try
			{
				return await QueryTopPlayersAsync(nameof(K4Ranks.PlayerData.Playtime), count);
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to get top players by time");
				return [];
			}
		}

		/// <summary>
		/// <c>ORDER BY ... DESC LIMIT</c>, valid on all three engines. Columns are aliased to the
		/// property names so Dapper maps them regardless of the column naming.
		/// </summary>
		private async Task<List<PlayerData>> QueryTopPlayersAsync(string orderProperty, int count)
		{
			using var connection = Core.Database.GetConnection(_connectionName);
			connection.Open();

			var sql = DommelMapper.GetSqlBuilder(connection);
			var type = typeof(PlayerData);

			var columns = Resolvers.Properties(type)
				.Select(p => $"{Resolvers.Column(p.Property, sql, false)} AS {sql.QuoteIdentifier(p.Property.Name)}");
			var orderBy = Resolvers.Column(type.GetProperty(orderProperty)!, sql, false);

			var query = $"SELECT {string.Join(", ", columns)} FROM {Resolvers.Table(type, sql)} ORDER BY {orderBy} DESC LIMIT {sql.PrefixParameter("Count")}";
			return [.. await connection.QueryAsync<PlayerData>(query, new { Count = count })];
		}
	}
}
