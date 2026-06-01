using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Extends the 'name' column from VARCHAR(64) to VARCHAR(128).
/// ASCII art player names frequently exceed the original 64-character limit,
/// causing the INSERT to fail and the player's rank to never be persisted.
/// </summary>
[Migration(20251201004)]
public class M004_ExtendNameColumn : Migration
{
	public override void Up()
	{
		// MySQL strict mode validates every existing row when any MODIFY COLUMN runs,
		// even for a size-only change. Rows that contain 4-byte UTF-8 bytes
		// (supplementary Unicode / emoji stored with strict mode OFF) are flagged as
		// "Incorrect string value" under utf8mb3. Disabling strict mode for this
		// session allows the ALTER to proceed without charset validation.
		// The SESSION-level change disappears when the connection closes.
		IfDatabase("MySql5").Execute.Sql("SET SESSION sql_mode = '';");
		Alter.Table("lvl_base")
			.AlterColumn("name").AsString(128).NotNullable().WithDefaultValue("");
	}

	public override void Down()
	{
		Alter.Table("lvl_base")
			.AlterColumn("name").AsString(64).NotNullable().WithDefaultValue("");
	}
}
