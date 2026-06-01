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
		if (Schema.Table("lvl_base_weapons").Exists())
			return;

		Create.Table("lvl_base_weapons")
			.WithColumn("steam").AsString(32).NotNullable()
			.WithColumn("classname").AsString(64).NotNullable()
			.WithColumn("kills").AsInt32().NotNullable().WithDefaultValue(0)
			// K4 Extensions
			.WithColumn("deaths").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("headshots").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("hits").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("shots").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("damage").AsInt64().NotNullable().WithDefaultValue(0);

		Create.PrimaryKey("pk_lvl_base_weapons")
			.OnTable("lvl_base_weapons")
			.Columns("steam", "classname");

		Create.Index("idx_weapons_steam")
			.OnTable("lvl_base_weapons")
			.OnColumn("steam");
	}

	public override void Down()
	{
		Delete.Table("lvl_base_weapons");
	}
}
