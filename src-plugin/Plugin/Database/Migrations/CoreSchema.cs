using FluentMigrator.Builders.Create;
using FluentMigrator.Builders.Schema;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// Per-object create helpers for the core schema, shared by M001 (fresh install) and
/// M007 (repair). Every object is checked individually because the main table can
/// already exist without the rest - pointing the plugin at an existing LVL Ranks
/// database is a supported, documented setup.
/// </summary>
internal static class CoreSchema
{
	public static void EnsureBaseIndexes(ISchemaExpressionRoot schema, ICreateExpressionRoot create)
	{
		// Index names are schema-scoped on PostgreSQL and database-scoped on SQLite,
		// so they collide between prefixes unless they carry the prefix too.
		EnsureIndex(schema, create, "idx_value", "value", descending: true);
		EnsureIndex(schema, create, "idx_visiblerank", "rank");
		EnsureIndex(schema, create, "idx_lastconnect", "lastconnect");
	}

	public static void EnsureSettingsTable(ISchemaExpressionRoot schema, ICreateExpressionRoot create)
	{
		if (schema.Table(TableNames.Settings).Exists())
			return;

		create.Table(TableNames.Settings)
			.WithColumn("steam").AsString(32).NotNullable().PrimaryKey()
			.WithColumn("messages").AsBoolean().NotNullable().WithDefaultValue(true)
			.WithColumn("summary").AsBoolean().NotNullable().WithDefaultValue(false)
			.WithColumn("rankchanges").AsBoolean().NotNullable().WithDefaultValue(true);
	}

	private static void EnsureIndex(
		ISchemaExpressionRoot schema,
		ICreateExpressionRoot create,
		string indexName,
		string columnName,
		bool descending = false)
	{
		indexName = TableNames.Prefix + indexName;

		if (schema.Table(TableNames.Base).Index(indexName).Exists())
			return;

		var column = create.Index(indexName).OnTable(TableNames.Base).OnColumn(columnName);

		if (descending)
			column.Descending();
		else
			column.Ascending();
	}
}
