using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Converts the 'name' column in lvl_base to utf8mb4 on MySQL/MariaDB.
/// MySQL's default 'utf8' charset is actually utf8mb3, which only supports
/// Basic Multilingual Plane characters (≤ U+FFFF). Player names containing
/// characters outside the BMP — such as Mathematical Fraktur, emoji, or other
/// supplementary Unicode — require utf8mb4 (true 4-byte UTF-8).
/// PostgreSQL and SQLite already store all Unicode correctly, so no change is
/// needed on those engines.
/// </summary>
[Migration(20251201005)]
public class M005_NameColumnUtf8mb4 : Migration
{
	public override void Up()
	{
		// The name column may contain 4-byte UTF-8 byte sequences (e.g. Mathematical
		// Unicode / supplementary plane characters) that were inserted when the MySQL
		// connection or server had strict mode disabled. Those bytes are valid utf8mb4
		// but invalid utf8mb3, so MySQL's charset validation fires on ANY MODIFY COLUMN
		// operation — including converting utf8mb3 → VARBINARY — and raises
		// "Incorrect string value" even before we touch the charset.
		//
		// Fix: disable strict SQL validation for this session only so that
		//   Step 1 (→ VARBINARY) copies the raw bytes without source-charset validation.
		//   Step 2 (→ utf8mb4)   accepts all 4-byte sequences without data loss.
		// The SESSION-level sql_mode does not persist after the connection closes.
		IfDatabase("MySql5").Execute.Sql("SET SESSION sql_mode = '';");
		IfDatabase("MySql5").Execute.Sql(
			$"ALTER TABLE `{TableNames.Base}` MODIFY COLUMN `name` VARBINARY(512);"
		);
		IfDatabase("MySql5").Execute.Sql(
			$"ALTER TABLE `{TableNames.Base}` MODIFY COLUMN `name` VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '';"
		);
	}

	public override void Down()
	{
		IfDatabase("MySql5").Execute.Sql(
			$"ALTER TABLE `{TableNames.Base}` MODIFY COLUMN `name` VARCHAR(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT '';"
		);
	}
}
