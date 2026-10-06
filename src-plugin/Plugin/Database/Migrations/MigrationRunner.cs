using System.Data;
using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Extensions.DependencyInjection;

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
	/// <param name="dbConnection">Unopened database connection; only its connection string is read.</param>
	/// <param name="driver">SwiftlyS2's driver id: <c>mysql</c>, <c>postgresql</c> or <c>sqlite</c>.</param>
	public static void RunMigrations(IDbConnection dbConnection, string? driver)
	{
		// Not disposing leaks the processor and the connection it opened, which on SQLite
		// keeps the database file held open across a plugin reload.
		using var serviceProvider = new ServiceCollection()
			.AddFluentMigratorCore()
			.ConfigureRunner(rb =>
			{
				ConfigureDatabase(rb, driver);
				rb.ScanIn(typeof(MigrationRunner).Assembly).For.Migrations();
				rb.WithGlobalConnectionString(dbConnection.ConnectionString);
			})
			.AddLogging(lb => lb.AddFluentMigratorConsole())
			.BuildServiceProvider(false);

		using var scope = serviceProvider.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
		runner.MigrateUp();
	}

	/// <summary>
	/// Configure the FluentMigrator runner for the appropriate database type.
	/// </summary>
	/// <remarks>
	/// Keyed on SwiftlyS2's own driver id rather than the connection's CLR type: SwiftlyS2
	/// builds SQLite connections with System.Data.SQLite, so a type match against the
	/// Microsoft.Data.Sqlite type this plugin references never fires and every SQLite
	/// install fell through to the "unsupported" branch.
	/// </remarks>
	private static void ConfigureDatabase(IMigrationRunnerBuilder rb, string? driver)
	{
		switch (driver?.Trim().ToLowerInvariant())
		{
			case "mysql" or "mariadb":
				rb.AddMySql5();
				break;
			case "postgresql" or "postgres":
				rb.AddPostgres();
				break;
			case "sqlite":
				rb.AddSQLite();
				break;
			default:
				throw new NotSupportedException(
					$"Unsupported database driver '{driver}'. Supported drivers are mysql, postgresql and sqlite.");
		}

		// Without its own ledger a prefixed install shares VersionInfo with every other
		// prefix in the database, sees all migrations as already applied, and never gets
		// its tables created. Left alone when unprefixed so existing installs keep
		// FluentMigrator's own default verbatim.
		if (TableNames.HasPrefix)
			rb.WithVersionTable(new PrefixedVersionTableMetaData());
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
