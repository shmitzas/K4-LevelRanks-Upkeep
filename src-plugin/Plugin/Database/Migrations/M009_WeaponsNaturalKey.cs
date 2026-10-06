using System.Data;
using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Gives lvl_base_weapons the (steam, classname) key the weapon upsert depends on.
/// M002 leaves a table that already exists untouched, so a database carried over from
/// LVL Ranks ExStats can reach this point without one - and then MySQL's
/// ON DUPLICATE KEY UPDATE silently appends a row on every save, while PostgreSQL and
/// SQLite reject the ON CONFLICT clause outright.
/// </summary>
[Migration(20251201009)]
public class M009_WeaponsNaturalKey : Migration
{
	private const string PrimaryKeyProbe =
		"SELECT COUNT(*) FROM information_schema.table_constraints " +
		"WHERE table_schema = {0} AND table_name = '{1}' AND constraint_type = 'PRIMARY KEY'";

	private static string IndexName => TableNames.Apply("ux_lvl_base_weapons_steam_classname");

	public override void Up()
	{
		if (!Schema.Table(TableNames.Weapons).Exists() || Schema.Table(TableNames.Weapons).Index(IndexName).Exists())
			return;

		// A unique index rather than a primary key: it is the only form all three engines
		// accept on a table that already holds rows, and ON CONFLICT / ON DUPLICATE KEY
		// are satisfied by either.
		// "PostgreSQL" is the alias to match: AddPostgres() reports a version-stamped
		// DatabaseType (PostgreSQL15_0), so "Postgres" silently matches nothing.
		IfDatabase("MySql5").Execute.WithConnection((c, t) => EnsureKey(c, t,
			string.Format(PrimaryKeyProbe, "DATABASE()", TableNames.Weapons),
			$"CREATE UNIQUE INDEX `{IndexName}` ON `{TableNames.Weapons}` (`steam`, `classname`)"));

		IfDatabase("PostgreSQL").Execute.WithConnection((c, t) => EnsureKey(c, t,
			string.Format(PrimaryKeyProbe, "current_schema()", TableNames.Weapons),
			$"CREATE UNIQUE INDEX \"{IndexName}\" ON \"{TableNames.Weapons}\" (\"steam\", \"classname\")"));

		// SQLite exposes no constraint catalogue; the implicit index behind a composite
		// primary key shows up as pk > 0 on the key columns.
		IfDatabase("SQLite").Execute.WithConnection((c, t) => EnsureKey(c, t,
			$"SELECT COUNT(*) FROM pragma_table_info('{TableNames.Weapons}') WHERE pk > 0",
			$"CREATE UNIQUE INDEX \"{IndexName}\" ON \"{TableNames.Weapons}\" (\"steam\", \"classname\")"));
	}

	public override void Down()
	{
		// Left in place: without it the weapon upsert cannot work, and M002 owns the key
		// it creates itself.
	}

	/// <summary>
	/// Fails loudly when the index cannot be created - duplicate rows mean the broken
	/// upsert has already been running, and continuing would keep adding to them.
	/// </summary>
	private static void EnsureKey(IDbConnection connection, IDbTransaction? transaction, string primaryKeyProbe, string createIndex)
	{
		using (var probe = connection.CreateCommand())
		{
			probe.Transaction = transaction;
			probe.CommandText = primaryKeyProbe;

			if (Convert.ToInt64(probe.ExecuteScalar()) > 0)
				return;
		}

		using var create = connection.CreateCommand();
		create.Transaction = transaction;
		create.CommandText = createIndex;
		create.ExecuteNonQuery();
	}
}
