using System.Collections.Concurrent;
using System.Data.Common;
using Dapper;
using Dommel;

namespace K4Ranks;

public sealed partial class Plugin
{
	public sealed partial class DatabaseService
	{
		// =========================================
		// =           SAVE OPERATIONS
		// =========================================

		private static readonly ConcurrentDictionary<(Type Entity, Type Dialect), string> UpsertSqlCache = new();

		/// <summary>One connection for a whole save pass. Throws when the database is unreachable.</summary>
		internal async Task<DbConnection> OpenSaveConnectionAsync(CancellationToken ct)
		{
			if (!IsEnabled)
				throw new InvalidOperationException("Database is not enabled");

			if (Core.Database.GetConnection(_connectionName) is not DbConnection connection)
				throw new NotSupportedException("The configured database connection is not a DbConnection");

			try
			{
				await connection.OpenAsync(ct);
				return connection;
			}
			catch
			{
				await connection.DisposeAsync();
				throw;
			}
		}

		/// <summary>
		/// Writes one player's changed rows in a single transaction. On any failure the transaction
		/// is rolled back and the exception propagates, so the caller keeps the record pending.
		/// </summary>
		internal async Task SavePlayerAsync(DbConnection connection, PlayerSaveEntry entry, CancellationToken ct)
		{
			await using var transaction = await connection.BeginTransactionAsync(ct);

			try
			{
				if (entry.Reset)
				{
					await DeleteWeaponStatsAsync(connection, transaction, entry.Steam, ct);
					await DeleteHitDataAsync(connection, transaction, entry.Steam, ct);
				}

				if (entry.Base != null)
					await UpsertAsync(connection, transaction, entry.Base, ct);

				if (entry.Settings != null)
					await UpsertAsync(connection, transaction, entry.Settings, ct);

				if (_modules.WeaponStatsEnabled && entry.Weapons != null)
				{
					foreach (var weapon in entry.Weapons)
						await UpsertAsync(connection, transaction, weapon, ct);
				}

				if (_modules.HitStatsEnabled && entry.Hits != null)
					await UpsertAsync(connection, transaction, entry.Hits, ct);

				await transaction.CommitAsync(ct);
			}
			catch
			{
				try
				{
					await transaction.RollbackAsync(CancellationToken.None);
				}
				catch
				{
					// A dead connection has nothing left to roll back; the server discards it.
				}

				throw;
			}
		}

		/// <summary>
		/// Single-statement insert-or-update keyed on the entity's primary key: no read-then-write
		/// race, and no duplicate-key error that would abort a PostgreSQL transaction.
		/// </summary>
		private static Task UpsertAsync<T>(DbConnection connection, DbTransaction transaction, T row, CancellationToken ct) where T : class
		{
			var sql = DommelMapper.GetSqlBuilder(connection);
			var text = UpsertSqlCache.GetOrAdd((typeof(T), sql.GetType()), key => BuildUpsertSql(key.Entity, sql));

			return connection.ExecuteAsync(new CommandDefinition(text, row, transaction, cancellationToken: ct));
		}

		/// <remarks>
		/// MySQL/MariaDB: <c>ON DUPLICATE KEY UPDATE c = VALUES(c)</c> — MariaDB has no row alias, and
		/// MySQL 8.0.20+ only deprecates VALUES() here. PostgreSQL (9.5+) and SQLite (3.24+):
		/// <c>ON CONFLICT (key) DO UPDATE SET c = excluded.c</c>. Table and column names come from
		/// Dommel's resolvers, so the table prefix and [Column] mapping apply, and every identifier
		/// is quoted (<c>rank</c> is reserved in MySQL 8).
		/// </remarks>
		private static string BuildUpsertSql(Type entity, ISqlBuilder sql)
		{
			var columns = Resolvers.Properties(entity).Select(p => p.Property).ToList();
			var keys = Resolvers.KeyProperties(entity).Select(p => p.Property).ToHashSet();

			string Column(System.Reflection.PropertyInfo property) => Resolvers.Column(property, sql, false);

			var insert = $"INSERT INTO {Resolvers.Table(entity, sql)} ({string.Join(", ", columns.Select(Column))}) " +
				$"VALUES ({string.Join(", ", columns.Select(p => sql.PrefixParameter(p.Name)))})";
			var updated = columns.Where(p => !keys.Contains(p)).Select(Column).ToList();

			return sql switch
			{
				MySqlSqlBuilder => $"{insert} ON DUPLICATE KEY UPDATE {string.Join(", ", updated.Select(c => $"{c} = VALUES({c})"))}",
				PostgresSqlBuilder or SqliteSqlBuilder => $"{insert} ON CONFLICT ({string.Join(", ", keys.Select(Column))}) " +
					$"DO UPDATE SET {string.Join(", ", updated.Select(c => $"{c} = excluded.{c}"))}",
				_ => throw new NotSupportedException($"No upsert dialect for {sql.GetType().Name}")
			};
		}
	}
}
