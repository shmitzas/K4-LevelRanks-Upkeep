using Dommel;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace K4Ranks;

public sealed partial class Plugin
{
	public sealed partial class DatabaseService
	{
		// =========================================
		// =           LOAD OPERATIONS
		// =========================================

		/// <summary>
		/// Loads a player row from the database.
		/// Returns the row if found, or <c>null</c> if no row exists for the given SteamID.
		/// Throws on transient/database errors so callers can distinguish "new player"
		/// from "load failed" — critical to avoid overwriting a real row with default
		/// starting points after a transient DB hiccup.
		/// </summary>
		public async Task<PlayerData?> LoadPlayerAsync(string steamId)
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
					return await connection.GetAsync<PlayerData>(steamId);
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

			Core.Logger.LogError(lastError, "Failed to load player {Steam} after {Max} attempts", steamId, maxAttempts);
			throw lastError!;
		}

		// =========================================
		// =           SAVE OPERATIONS
		// =========================================

		/// <summary>
		/// Saves a player row using UPDATE-first semantics. If no row was updated
		/// (i.e., the player is genuinely new), falls back to INSERT. This ordering
		/// is safer than INSERT-then-UPDATE because a duplicate-PK INSERT can mask
		/// a corrupt/default in-memory record being written over a real DB row.
		/// </summary>
		public async Task SavePlayerAsync(PlayerData data)
		{
			if (!IsEnabled)
				return;

			try
			{
				data.LastConnect = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				var updated = await connection.UpdateAsync(data);
				if (!updated)
				{
					try
					{
						await connection.InsertAsync(data);
					}
					catch (MySqlException ex) when (ex.Number == 1062)
					{
						// Race: row was created between UPDATE and INSERT — retry update.
						await connection.UpdateAsync(data);
					}
				}

				data.IsDirty = false;
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to save player {Steam}", data.Steam);
			}
		}

		public async Task SavePlayersAsync(IEnumerable<PlayerData> players)
		{
			if (!IsEnabled)
				return;

			var dirty = players.Where(p => p.IsDirty && p.IsLoaded).ToList();
			if (dirty.Count == 0)
				return;

			try
			{
				var now = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				foreach (var data in dirty)
				{
					data.LastConnect = now;

					var updated = await connection.UpdateAsync(data);
					if (!updated)
					{
						try
						{
							await connection.InsertAsync(data);
						}
						catch (MySqlException ex) when (ex.Number == 1062)
						{
							await connection.UpdateAsync(data);
						}
					}

					data.IsDirty = false;
				}
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to batch save players");
			}
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

				// Count players with higher value + 1
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

				// Count players with higher playtime + 1
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
				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				var allPlayers = await connection.GetAllAsync<PlayerData>();
				return [.. allPlayers
					.OrderByDescending(p => p.Value)
					.Take(count)];
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
				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				var allPlayers = await connection.GetAllAsync<PlayerData>();
				return [.. allPlayers
					.OrderByDescending(p => p.Playtime)
					.Take(count)];
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to get top players by time");
				return [];
			}
		}
	}
}
