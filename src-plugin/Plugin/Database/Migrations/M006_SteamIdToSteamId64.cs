using System.Data;
using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Converts steam ID keys from STEAM_X:Y:Z format to SteamID64 string format
/// across all lvl_base tables.
/// </summary>
[Migration(20251201006)]
public class M006_SteamIdToSteamId64 : Migration
{
	// Table name → steam column name
	private static (string Table, string Column)[] Tables =>
	[
		(TableNames.Base,     "steam"),
		(TableNames.Settings, "steam"),
		(TableNames.Weapons,  "steam"),
		(TableNames.Hits,     "SteamID"),
	];

	private enum Dialect { MySql, Postgres, Sqlite }

	/// <remarks>
	/// Dispatched through IfDatabase rather than by testing the connection's CLR type.
	/// FluentMigrator opens its own connection through its own reflectively-loaded ADO.NET
	/// provider, so a type the migration does not recognise used to fall through to the
	/// SQLite branch and run SQLite-only string functions against MySQL.
	/// "PostgreSQL" is the alias to match: AddPostgres() reports a version-stamped
	/// DatabaseType (PostgreSQL15_0), so "Postgres" silently matches nothing.
	/// </remarks>
	public override void Up()
	{
		IfDatabase("MySql5").Execute.WithConnection((c, t) => ConvertSteamIds(c, t, Dialect.MySql));
		IfDatabase("PostgreSQL").Execute.WithConnection((c, t) => ConvertSteamIds(c, t, Dialect.Postgres));
		IfDatabase("SQLite").Execute.WithConnection((c, t) => ConvertSteamIds(c, t, Dialect.Sqlite));
	}

	private static void ConvertSteamIds(IDbConnection connection, IDbTransaction? transaction, Dialect dialect)
	{
		foreach (var (table, column) in Tables)
		{
			if (!TableExists(connection, transaction, table, dialect))
				continue;

			using var cmd = connection.CreateCommand();
			cmd.Transaction = transaction;
			cmd.CommandText = BuildConvertSql(table, column, dialect);
			cmd.ExecuteNonQuery();
		}
	}

	public override void Down()
	{
		// Conversion back to STEAM_X:Y:Z is not feasible in pure SQL.
		// Down migration is intentionally left empty.
	}

	/// <summary>
	/// Scoped to the current database/schema: an unqualified information_schema lookup
	/// also matches a same-named table owned by somebody else on a shared server, and the
	/// UPDATE that follows would then run against a table that is not there.
	/// </summary>
	private static bool TableExists(
		IDbConnection connection,
		IDbTransaction? transaction,
		string table,
		Dialect dialect)
	{
		string sql = dialect switch
		{
			Dialect.MySql => $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = '{table}'",
			Dialect.Postgres => $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '{table}'",
			_ => $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'"
		};

		using var cmd = connection.CreateCommand();
		cmd.Transaction = transaction;
		cmd.CommandText = sql;

		return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
	}

	/// <summary>
	/// Builds the UPDATE statement that converts STEAM_X:Y:Z values to SteamID64.
	/// Formula: SteamID64 = 76561197960265728 + Z*2 + Y
	///   where STEAM_X:Y:Z — Y is auth bit, Z is account number.
	/// Only rows whose column still matches the STEAM_ prefix are touched.
	/// </summary>
	private static string BuildConvertSql(string table, string column, Dialect dialect)
	{
		if (dialect == Dialect.MySql)
		{
			// SUBSTRING_INDEX is MySQL-specific
			return
				$"UPDATE `{table}` " +
				$"SET `{column}` = CAST(" +
				$"  76561197960265728 " +
				$"  + CAST(SUBSTRING_INDEX(`{column}`, ':', -1) AS UNSIGNED) * 2 " +
				$"  + CAST(SUBSTRING_INDEX(SUBSTRING_INDEX(`{column}`, ':', 2), ':', -1) AS UNSIGNED) " +
				$"  AS CHAR) " +
				$"WHERE `{column}` LIKE 'STEAM_%'";
		}

		if (dialect == Dialect.Postgres)
		{
			// split_part is PostgreSQL-specific
			return
				$"UPDATE \"{table}\" " +
				$"SET \"{column}\" = (" +
				$"  76561197960265728 " +
				$"  + CAST(split_part(\"{column}\", ':', 3) AS BIGINT) * 2 " +
				$"  + CAST(split_part(\"{column}\", ':', 2) AS BIGINT)" +
				$")::TEXT " +
				$"WHERE \"{column}\" LIKE 'STEAM_%'";
		}

		// SQLite — use nested INSTR/SUBSTR
		// For 'STEAM_X:Y:Z':
		//   c1  = position of first ':'
		//   c2r = position of second ':' relative to the substring after c1
		//   Y   = character(s) between c1 and c2 relative to start
		//   Z   = everything after c2
		return
			$"UPDATE \"{table}\" " +
			$"SET \"{column}\" = CAST(" +
			$"  76561197960265728 " +
			$"  + CAST(SUBSTR(\"{column}\", " +
			$"      INSTR(\"{column}\", ':') + " +
			$"      INSTR(SUBSTR(\"{column}\", INSTR(\"{column}\", ':') + 1), ':') + 1) AS INTEGER) * 2 " +
			$"  + CAST(SUBSTR(" +
			$"      SUBSTR(\"{column}\", INSTR(\"{column}\", ':') + 1), " +
			$"      1, " +
			$"      INSTR(SUBSTR(\"{column}\", INSTR(\"{column}\", ':') + 1), ':') - 1) AS INTEGER) " +
			$"  AS TEXT) " +
			$"WHERE \"{column}\" LIKE 'STEAM_%'";
	}
}
