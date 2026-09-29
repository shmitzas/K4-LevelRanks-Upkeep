using Dommel;

namespace K4Ranks;

/// <summary>
/// Physical table names, derived from the optional <c>Database.TablePrefix</c> config value.
/// An empty prefix reproduces the stock LVL Ranks names exactly.
/// </summary>
/// <remarks>
/// Static because neither consumer can be handed the prefix through a constructor:
/// FluentMigrator instantiates migrations itself, and Dommel's table-name hook is a
/// process-wide singleton. Configured once from <see cref="Plugin.DatabaseService.InitializeAsync"/>
/// before any query can run.
/// </remarks>
internal static class TableNames
{
	internal static string Prefix { get; private set; } = "";

	internal static bool HasPrefix => Prefix.Length > 0;

	internal static string Base => Prefix + "lvl_base";
	internal static string Settings => Prefix + "lvl_base_settings";
	internal static string Weapons => Prefix + "lvl_base_weapons";
	internal static string Hits => Prefix + "lvl_base_hits";

	/// <summary>
	/// FluentMigrator's applied-migrations ledger. Prefixed too, otherwise two servers
	/// sharing one database would share one ledger: the second one to boot would see
	/// every migration already applied and never create its own tables.
	/// </summary>
	internal static string VersionInfo => Prefix + "VersionInfo";

	internal static string VersionInfoIndex => Prefix + "UC_Version";

	internal static void Configure(string? prefix)
	{
		prefix = prefix?.Trim() ?? "";

		// Reaches raw DDL in M005/M006, so it is validated rather than escaped.
		if (prefix.Length > 0 && !prefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
			throw new InvalidOperationException(
				$"Database.TablePrefix '{prefix}' is invalid - only letters, digits and underscores are allowed.");

		Prefix = prefix;
	}
}

/// <summary>
/// Prepends the configured prefix to the <c>[Table]</c> name of this plugin's entities.
/// </summary>
internal sealed class PrefixedTableNameResolver : ITableNameResolver
{
	private static readonly DefaultTableNameResolver Default = new();

	public string ResolveTableName(Type type)
	{
		var name = Default.ResolveTableName(type);

		// Dommel's resolver hook is process-wide; never rename another plugin's entities.
		return type.Assembly == typeof(PrefixedTableNameResolver).Assembly
			? TableNames.Prefix + name
			: name;
	}
}
