using Dommel;

namespace K4Ranks;

/// <summary>
/// Physical table names, derived from the optional <c>Database.TablePrefix</c> and
/// <c>Database.TableSuffix</c> config values. Both empty reproduces the stock LVL Ranks
/// names exactly.
/// </summary>
/// <remarks>
/// Static because neither consumer can be handed the affixes through a constructor:
/// FluentMigrator instantiates migrations itself, and Dommel's table-name hook is a
/// process-wide singleton. Configured once from <see cref="Plugin.DatabaseService.InitializeAsync"/>
/// before any query can run.
/// </remarks>
internal static class TableNames
{
	internal static string Prefix { get; private set; } = "";

	internal static string Suffix { get; private set; } = "";

	internal static bool HasAffix => Prefix.Length > 0 || Suffix.Length > 0;

	internal static string Base => Apply("lvl_base");
	internal static string Settings => Apply("lvl_base_settings");
	internal static string Weapons => Apply("lvl_base_weapons");
	internal static string Hits => Apply("lvl_base_hits");

	/// <summary>
	/// FluentMigrator's applied-migrations ledger. Affixed too, otherwise two servers
	/// sharing one database would share one ledger: the second one to boot would see
	/// every migration already applied and never create its own tables.
	/// </summary>
	internal static string VersionInfo => Apply("VersionInfo");

	internal static string VersionInfoIndex => Apply("UC_Version");

	/// <summary>Attaches the configured affixes to a table, index or constraint name.</summary>
	internal static string Apply(string name) => Prefix + name + Suffix;

	internal static void Configure(string? prefix, string? suffix, string? retiredPosition)
	{
		// Ignoring a leftover TablePrefixPosition would move an existing install's tables
		// without saying so. Harmless when there is no prefix for it to have positioned.
		if (!string.IsNullOrWhiteSpace(retiredPosition) && !string.IsNullOrWhiteSpace(prefix))
			throw new InvalidOperationException(
				"Database.TablePrefixPosition has been replaced by Database.TableSuffix. Set " +
				"TablePrefix for awp_lvl_base or TableSuffix for lvl_base_awp, then delete " +
				"TablePrefixPosition. Existing tables are not renamed for you.");

		Prefix = Normalize(prefix, "Database.TablePrefix", separatorLast: true);
		Suffix = Normalize(suffix, "Database.TableSuffix", separatorLast: false);
	}

	/// <summary>
	/// Trims the operator's own underscores and supplies exactly one on the joining side, so
	/// "awp", "awp_" and "_awp" all name the same tables. Without it, tidying the value later
	/// points the server at a fresh, empty table set that looks like losing every player.
	/// </summary>
	private static string Normalize(string? value, string key, bool separatorLast)
	{
		var trimmed = value?.Trim().Trim('_') ?? "";
		if (trimmed.Length == 0)
			return "";

		// Reaches raw DDL in M005/M006/M009, so it is validated rather than escaped.
		if (!trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
			throw new InvalidOperationException(
				$"{key} '{value}' is invalid - only letters, digits and underscores are allowed.");

		return separatorLast ? trimmed + "_" : "_" + trimmed;
	}
}

/// <summary>
/// Attaches the configured affixes to the <c>[Table]</c> name of this plugin's entities.
/// </summary>
internal sealed class AffixedTableNameResolver : ITableNameResolver
{
	private static readonly DefaultTableNameResolver Default = new();

	public string ResolveTableName(Type type)
	{
		var name = Default.ResolveTableName(type);

		// Dommel's resolver hook is process-wide; never rename another plugin's entities.
		return type.Assembly == typeof(AffixedTableNameResolver).Assembly
			? TableNames.Apply(name)
			: name;
	}
}
