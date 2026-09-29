using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Repairs databases where M001 bailed out on seeing an existing lvl_base and therefore
/// never created the settings table or the indexes - typically an install pointed at an
/// existing LVL Ranks database, or a first run that failed after the main table was
/// created (MySQL DDL is not transactional, so the table survived while VersionInfo was
/// not written, and the retry hit the same early return).
/// M001 is already recorded as applied on those databases, so MigrateUp will never
/// re-run it and the missing objects need a migration of their own.
/// </summary>
[Migration(20251201007)]
public class M007_EnsureCoreObjects : Migration
{
	public override void Up()
	{
		CoreSchema.EnsureBaseIndexes(Schema, Create);
		CoreSchema.EnsureSettingsTable(Schema, Create);
	}

	public override void Down()
	{
		// Nothing to undo - M001 owns these objects.
	}
}
