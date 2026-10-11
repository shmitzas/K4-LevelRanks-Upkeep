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
		private readonly string _tableSuffix;
		private readonly string _retiredPrefixPosition;
		private readonly int _purgeDays;
		private readonly ModuleConfig _modules;

		/* ==================== Properties ==================== */

		public bool IsEnabled { get; private set; }

		/* ==================== Constructor ==================== */

		public DatabaseService(string connectionName, string tablePrefix, string tableSuffix, string retiredPrefixPosition, int purgeDays, ModuleConfig modules)
		{
			_connectionName = connectionName;
			_tablePrefix = tablePrefix;
			_tableSuffix = tableSuffix;
			_retiredPrefixPosition = retiredPrefixPosition;
			_purgeDays = purgeDays;
			_modules = modules;
		}

		/* ==================== Initialization ==================== */

		public async Task InitializeAsync()
		{
			try
			{
				// Both hooks are global and must be in place before the first query. Every
				// public method here is gated on IsEnabled, which stays false until they are.
				TableNames.Configure(_tablePrefix, _tableSuffix, _retiredPrefixPosition);
				DommelMapper.SetTableNameResolver(new AffixedTableNameResolver());

				WarnIfPrefixMeaningChanged();

				using var connection = Core.Database.GetConnection(_connectionName);

				Exception? schemaError = null;
				try
				{
					// Must come before anything opens the connection: MySqlConnector strips
					// the password from ConnectionString on open, and FluentMigrator builds
					// its own connection from that string.
					MigrationRunner.RunMigrations(connection, Core.Database.GetConnectionInfo(_connectionName).Driver);
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
				{
					if (IsPermissionDenied(schemaError))
						Core.Logger.LogWarning(schemaError,
							"Schema migrations failed because the database user has no DDL permissions, but every required table is already usable - continuing. Schema changes shipped by future plugin updates will not be applied until this is resolved.");
					else
						// Not an environment problem the operator chose: report it as the
						// defect it is, even though the plugin can keep running.
						Core.Logger.LogError(schemaError,
							"Schema migrations failed, but every required table is already usable - continuing. Schema changes shipped by future plugin updates will not be applied until this is resolved.");
				}

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
		/// <remarks>
		/// Only ever sees exceptions raised by FluentMigrator, which opens its own connection
		/// through its own provider - Microsoft.Data.Sqlite on SQLite, not the
		/// System.Data.SQLite connection SwiftlyS2 hands this plugin.
		/// </remarks>
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

		/// <summary>
		/// v1.4.0 and earlier used the prefix verbatim, so "awp" named awplvl_base. The
		/// separator is supplied now, which silently points such an install at an empty
		/// table set - the one case the operator has to act on, so say it out loud.
		/// </summary>
		private void WarnIfPrefixMeaningChanged()
		{
			var legacy = (_tablePrefix ?? "").Trim() + "lvl_base";

			if (legacy == "lvl_base" || legacy == TableNames.Base)
				return;

			Core.Logger.LogWarning(
				"Database.TablePrefix '{Prefix}' now resolves to {Table}; before v1.5.0 it resolved to {Legacy}. Rename the old tables, or ignore this on a fresh install.",
				_tablePrefix, TableNames.Base, legacy);
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
	}
}
