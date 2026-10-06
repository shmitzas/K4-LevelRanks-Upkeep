using FluentMigrator;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Indexes the leaderboard columns so top-N and position queries no longer scan lvl_base.
/// A user without INDEX rights fails here and the plugin continues on the existing tables.
/// </summary>
[Migration(20251201008)]
public class M008_RankingIndexes : Migration
{
	public override void Up()
	{
		CoreSchema.EnsureRankingIndexes(Schema, Create);
	}

	public override void Down()
	{
		// Left in place: idx_value belongs to M001, and the playtime index is harmless.
	}
}
