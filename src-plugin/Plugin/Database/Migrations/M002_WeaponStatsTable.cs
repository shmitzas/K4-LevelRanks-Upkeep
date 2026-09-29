using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Weapon Stats module migration
/// Creates lvl_base_weapons table (LVL Ranks ExStats Weapons compatible)
/// </summary>
[Migration(20251201002)]
public class M002_WeaponStatsTable : Migration
{
	public override void Up()
	{
		if (Schema.Table(TableNames.Weapons).Exists())
			return;

		Create.Table(TableNames.Weapons)
			.WithColumn("steam").AsString(32).NotNullable()
			.WithColumn("classname").AsString(64).NotNullable()
			.WithColumn("kills").AsInt32().NotNullable().WithDefaultValue(0)
			// K4 Extensions
			.WithColumn("deaths").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("headshots").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("hits").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("shots").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("damage").AsInt64().NotNullable().WithDefaultValue(0);

		// Constraint and index names are schema-scoped on PostgreSQL and database-scoped
		// on SQLite, so they collide between prefixes unless they carry the prefix too.
		Create.PrimaryKey(TableNames.Prefix + "pk_lvl_base_weapons")
			.OnTable(TableNames.Weapons)
			.Columns("steam", "classname");

		Create.Index(TableNames.Prefix + "idx_weapons_steam")
			.OnTable(TableNames.Weapons)
			.OnColumn("steam");
	}

	public override void Down()
	{
		Delete.Table(TableNames.Weapons);
	}
}
