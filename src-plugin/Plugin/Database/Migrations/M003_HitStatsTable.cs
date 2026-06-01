using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Hit Stats module migration
/// Creates lvl_base_hits table (LVL Ranks ExStats Hits compatible)
/// </summary>
[Migration(20251201003)]
public class M003_HitStatsTable : Migration
{
	public override void Up()
	{
		if (Schema.Table("lvl_base_hits").Exists())
			return;

		Create.Table("lvl_base_hits")
			.WithColumn("SteamID").AsString(32).NotNullable().PrimaryKey()
			.WithColumn("DmgHealth").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("DmgArmor").AsInt64().NotNullable().WithDefaultValue(0)
			.WithColumn("Head").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("Chest").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("Belly").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("LeftArm").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("RightArm").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("LeftLeg").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("RightLeg").AsInt32().NotNullable().WithDefaultValue(0)
			.WithColumn("Neak").AsInt32().NotNullable().WithDefaultValue(0);
	}

	public override void Down()
	{
		Delete.Table("lvl_base_hits");
	}
}
