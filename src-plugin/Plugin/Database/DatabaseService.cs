using System.Data;
using Dommel;
using K4Ranks.Database.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;

namespace K4Ranks;

public sealed partial class Plugin
{
	/// <summary>
	/// Database service - LVL Ranks compatible structure
	/// </summary>
	public sealed partial class DatabaseService
	{
		/* ==================== Fields ==================== */

		private readonly string _connectionName;
		private readonly string _tablePrefix;
		private readonly int _purgeDays;
		private readonly int _startPoints;
		private readonly ModuleConfig _modules;

		/* ==================== Properties ==================== */

		public bool IsEnabled { get; private set; }

		/* ==================== Constructor ==================== */

		public DatabaseService(string connectionName, string tablePrefix, int purgeDays, int startPoints, ModuleConfig modules)
		{
			_connectionName = connectionName;
			_tablePrefix = tablePrefix;
			_purgeDays = purgeDays;
			_startPoints = startPoints;
			_modules = modules;
		}

		/* ==================== Initialization ==================== */

		public async Task InitializeAsync()
		{
			try
			{
				// Both hooks are global and must be in place before the first query. Every
				// public method here is gated on IsEnabled, which stays false until they are.
				TableNames.Configure(_tablePrefix);
				DommelMapper.SetTableNameResolver(new PrefixedTableNameResolver());

				using var connection = Core.Database.GetConnection(_connectionName);

				Exception? schemaError = null;
				try
				{
					// Must come before anything opens the connection: MySqlConnector strips
					// the password from ConnectionString on open, and FluentMigrator builds
					// its own connection from that string.
					MigrationRunner.RunMigrations(connection);
				}
				catch (Exception ex)
				{
					// Migrations are only how the schema normally appears. Shared hosting
					// often grants the database user DML but no DDL, and the plugin runs
					// fine against tables that were created by hand or by LVL Ranks.
					schemaError = ex;
				}

				var unusable = FindUnusableTables(connection);
				if (unusable.Count > 0)
				{
					ReportUnusableSchema(schemaError, unusable);
					IsEnabled = false;
					return;
				}

				if (schemaError != null)
					Core.Logger.LogWarning(schemaError,
						"Schema migrations failed{Reason}, but every required table is already usable - continuing. Schema changes shipped by future plugin updates will not be applied until this is resolved.",
						IsPermissionDenied(schemaError) ? " because the database user has no DDL permissions" : "");

				IsEnabled = true;

				LogInitializedTables();
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to initialize database");
				IsEnabled = false;
			}
		}

		/// <summary>
		/// Probes each required table with a no-op read. Tests what the plugin actually
		/// needs - a readable table - rather than mere existence, and works on all three
		/// engines without provider-specific metadata queries.
		/// </summary>
		private List<string> FindUnusableTables(IDbConnection connection)
		{
			// A connection that cannot be opened at all is not a schema problem; let it
			// throw so the caller reports it as the connection failure it is.
			if (connection.State != ConnectionState.Open)
				connection.Open();

			var unusable = new List<string>();

			foreach (var table in RequiredTables())
			{
				try
				{
					using var command = connection.CreateCommand();
					command.CommandText = $"SELECT 1 FROM {table} WHERE 1 = 0";
					command.ExecuteScalar();
				}
				catch (Exception)
				{
					unusable.Add(table);
				}
			}

			return unusable;
		}

		private void ReportUnusableSchema(Exception? schemaError, List<string> unusable)
		{
			var tables = string.Join(", ", unusable);

			if (IsPermissionDenied(schemaError))
			{
				Core.Logger.LogError(schemaError,
					"Database disabled: the user lacks the permissions to create the required tables ({Tables}). Grant CREATE, ALTER and INDEX on this database, or create the tables manually.",
					tables);
				return;
			}

			Core.Logger.LogError(schemaError,
				"Database disabled: required tables are missing or cannot be read ({Tables})",
				tables);
		}

		/// <summary>
		/// True when the failure is the database user lacking rights rather than a broken
		/// schema, which is the difference between an actionable message and a stack trace.
		/// </summary>
		private static bool IsPermissionDenied(Exception? error)
		{
			for (var ex = error; ex != null; ex = ex.InnerException)
			{
				// Access denied to the database / table / column, and "you need privilege X".
				// 1045 is deliberately absent: that is a bad password, not a missing grant.
				if (ex is MySqlException mysql && mysql.Number is 1044 or 1142 or 1143 or 1227)
					return true;

				if (ex is PostgresException postgres && postgres.SqlState == "42501")
					return true;

				// SQLITE_READONLY / SQLITE_AUTH - the file or its directory is not writable.
				if (ex is SqliteException sqlite && sqlite.SqliteErrorCode is 8 or 23)
					return true;
			}

			return false;
		}

		private List<string> RequiredTables()
		{
			var tables = new List<string> { TableNames.Base, TableNames.Settings };

			if (_modules.WeaponStatsEnabled)
				tables.Add(TableNames.Weapons);

			if (_modules.HitStatsEnabled)
				tables.Add(TableNames.Hits);

			return tables;
		}

		private void LogInitializedTables()
			=> Core.Logger.LogInformation("Database initialized. Tables: {Tables}", string.Join(", ", RequiredTables()));

		/* ==================== Maintenance ==================== */

		public async Task PurgeOldDataAsync()
		{
			if (!IsEnabled || _purgeDays <= 0)
				return;

			try
			{
				var cutoffTimestamp = (int)DateTimeOffset.UtcNow.AddDays(-_purgeDays).ToUnixTimeSeconds();

				using var connection = Core.Database.GetConnection(_connectionName);
				connection.Open();

				// Dommel doesn't support complex WHERE with AND, use Dapper for this
				var deleted = await connection.DeleteMultipleAsync<PlayerData>(
					p => p.LastConnect < cutoffTimestamp && p.LastConnect > 0);

				if (deleted > 0)
					Core.Logger.LogInformation("Purged {Count} inactive players (>{Days} days)", deleted, _purgeDays);
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to purge old records");
			}
		}

		public async Task ResetPlayerAsync(string steamId)
		{
			if (!IsEnabled)
				return;

			try
			{
				await ResetPlayerStatsAsync(steamId);
				await ResetPlayerModuleDataAsync(steamId);
			}
			catch (Exception ex)
			{
				Core.Logger.LogError(ex, "Failed to reset player {Steam}", steamId);
			}
		}

		private async Task ResetPlayerStatsAsync(string steamId)
		{
			using var connection = Core.Database.GetConnection(_connectionName);
			connection.Open();

			var player = await connection.GetAsync<PlayerData>(steamId);
			if (player == null)
				return;

			// Reset all stats
			player.Value = _startPoints;
			player.Rank = 0;
			player.Kills = 0;
			player.Deaths = 0;
			player.Shoots = 0;
			player.Hits = 0;
			player.Headshots = 0;
			player.Assists = 0;
			player.RoundWin = 0;
			player.RoundLose = 0;
			player.Playtime = 0;
			player.GameWins = 0;
			player.GameLosses = 0;
			player.GamesPlayed = 0;
			player.RoundsPlayed = 0;
			player.Damage = 0;

			await connection.UpdateAsync(player);
		}

		private async Task ResetPlayerModuleDataAsync(string steamId)
		{
			using var connection = Core.Database.GetConnection(_connectionName);
			connection.Open();

			if (_modules.WeaponStatsEnabled)
			{
				await connection.DeleteMultipleAsync<WeaponStatRecord>(w => w.Steam == steamId);
			}

			if (_modules.HitStatsEnabled)
			{
				var hitData = await connection.GetAsync<HitData>(steamId);
				if (hitData != null)
				{
					await connection.DeleteAsync(hitData);
				}
			}
		}
	}
}
