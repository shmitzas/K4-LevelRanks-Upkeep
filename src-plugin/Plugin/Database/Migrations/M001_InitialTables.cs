using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Core tables migration - always runs
/// Creates lvl_base and lvl_base_settings tables
/// </summary>
[Migration(20251201001)]
public class M001_CoreTables : Migration
{
	public override void Up()
	{
		if (!Schema.Table(TableNames.Base).Exists())
		{
			Create.Table(TableNames.Base)
				.WithColumn("steam").AsString(32).NotNullable().PrimaryKey()
				.WithColumn("name").AsString(64).NotNullable().WithDefaultValue("")
				.WithColumn("value").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("rank").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("kills").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("deaths").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("shoots").AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("hits").AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("headshots").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("assists").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("round_win").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("round_lose").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("playtime").AsInt64().NotNullable().WithDefaultValue(0)
				.WithColumn("lastconnect").AsInt32().NotNullable().WithDefaultValue(0)
				// K4 Extensions
				.WithColumn("game_wins").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("game_losses").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("games_played").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("rounds_played").AsInt32().NotNullable().WithDefaultValue(0)
				.WithColumn("damage").AsInt64().NotNullable().WithDefaultValue(0);
		}

		// Checked separately rather than behind the guard above: an existing LVL Ranks
		// database already has lvl_base but none of these, and bailing out on the main
		// table left the settings table permanently missing.
		CoreSchema.EnsureBaseIndexes(Schema, Create);
		CoreSchema.EnsureSettingsTable(Schema, Create);
	}

	public override void Down()
	{
		Delete.Table(TableNames.Settings);
		Delete.Table(TableNames.Base);
	}
}
