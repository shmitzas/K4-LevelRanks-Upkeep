using System.Data;
using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;

namespace K4Ranks.Database.Migrations;

/// <summary>
/// FluentMigrator runner for database schema migrations
/// Supports MySQL/MariaDB, PostgreSQL, and SQLite
/// </summary>
public static class MigrationRunner
{
	/// <summary>
	/// Run all pending migrations
	/// </summary>
	/// <param name="dbConnection">Database connection</param>
	public static void RunMigrations(IDbConnection dbConnection)
	{
		var serviceProvider = new ServiceCollection()
			.AddFluentMigratorCore()
			.ConfigureRunner(rb =>
			{
				ConfigureDatabase(rb, dbConnection);
				rb.ScanIn(typeof(MigrationRunner).Assembly).For.Migrations();
			})
			.AddLogging(lb => lb.AddFluentMigratorConsole())
			.BuildServiceProvider(false);

		using var scope = serviceProvider.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
		runner.MigrateUp();
	}

	/// <summary>
	/// Configure the FluentMigrator runner for the appropriate database type
	/// </summary>
	private static void ConfigureDatabase(IMigrationRunnerBuilder rb, IDbConnection dbConnection)
	{
		switch (dbConnection)
		{
			case MySqlConnection:
				rb.AddMySql5();
				break;
			case NpgsqlConnection:
				rb.AddPostgres();
				break;
			case SqliteConnection:
				rb.AddSQLite();
				break;
			default:
				throw new NotSupportedException($"Unsupported database connection type: {dbConnection.GetType().Name}");
		}

		// Without its own ledger a prefixed install shares VersionInfo with every other
		// prefix in the database, sees all migrations as already applied, and never gets
		// its tables created. Left alone when unprefixed so existing installs keep
		// FluentMigrator's own default verbatim.
		if (TableNames.HasPrefix)
			rb.WithVersionTable(new PrefixedVersionTableMetaData());

		rb.WithGlobalConnectionString(dbConnection.ConnectionString);
	}

	/// <summary>
	/// Mirrors FluentMigrator's own defaults, with the prefix applied to the two
	/// identifiers that are not scoped to the table itself.
	/// </summary>
	private sealed class PrefixedVersionTableMetaData : IVersionTableMetaData
	{
		public string SchemaName => "";
		public string TableName => TableNames.VersionInfo;
		public string ColumnName => "Version";
		public string DescriptionColumnName => "Description";
		public string AppliedOnColumnName => "AppliedOn";
		public bool OwnsSchema => true;
		public bool CreateWithPrimaryKey => false;

		// Index names are schema-scoped on PostgreSQL and database-scoped on SQLite.
		public string UniqueIndexName => TableNames.VersionInfoIndex;
	}
}
