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

		// Declared inline rather than through Create.PrimaryKey: that emits
		// ALTER TABLE ... ADD CONSTRAINT, which SQLite rejects outright. Unnamed so
		// PostgreSQL derives the name from the table and prefixed installs cannot collide.
		// Its leftmost prefix also serves the per-player lookup, so no separate steam index.
		Create.Table(TableNames.Weapons)
			.WithColumn("steam").AsString(32).NotNullable().PrimaryKey()
			.WithColumn("classname").AsString(64).NotNullable().PrimaryKey()
			.WithColumn("kills").AsInt32().NotNullable().WithDefaultValue(0)
			// K4 Extensions
			.WithColumn("deaths").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("headshots").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("hits").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("shots").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("damage").AsInt64().NotNullable().WithDefaultValue(0);
	}

	public override void Down()
	{
		Delete.Table(TableNames.Weapons);
	}
}
