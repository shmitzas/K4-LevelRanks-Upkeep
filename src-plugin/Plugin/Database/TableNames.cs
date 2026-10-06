using Dommel;

namespace K4Ranks;

/// <summary>Where <c>Database.TablePrefix</c> is attached to a table name.</summary>
internal enum TablePrefixPosition
{
	Before,
	After
}

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

	internal static TablePrefixPosition Position { get; private set; } = TablePrefixPosition.Before;

	internal static bool HasPrefix => Prefix.Length > 0;

	internal static string Base => Apply("lvl_base");
	internal static string Settings => Apply("lvl_base_settings");
	internal static string Weapons => Apply("lvl_base_weapons");
	internal static string Hits => Apply("lvl_base_hits");

	/// <summary>
	/// FluentMigrator's applied-migrations ledger. Prefixed too, otherwise two servers
	/// sharing one database would share one ledger: the second one to boot would see
	/// every migration already applied and never create its own tables.
	/// </summary>
	internal static string VersionInfo => Apply("VersionInfo");

	internal static string VersionInfoIndex => Apply("UC_Version");

	/// <summary>
	/// Attaches the configured prefix to a table, index or constraint name.
	/// </summary>
	/// <remarks>
	/// Before mode keeps the configured value verbatim so installs already carrying "awp_"
	/// are untouched. After mode supplies the separator itself instead: it has no existing
	/// naming to preserve, and the same "awp_" would otherwise trail an underscore.
	/// </remarks>
	internal static string Apply(string name)
	{
		if (!HasPrefix)
			return name;

		if (Position == TablePrefixPosition.Before)
			return Prefix + name;

		var suffix = Prefix.Trim('_');
		return suffix.Length == 0 ? name : $"{name}_{suffix}";
	}

	internal static void Configure(string? prefix, string? position)
	{
		prefix = prefix?.Trim() ?? "";

		// Reaches raw DDL in M005/M006/M009, so it is validated rather than escaped.
		if (prefix.Length > 0 && !prefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
			throw new InvalidOperationException(
				$"Database.TablePrefix '{prefix}' is invalid - only letters, digits and underscores are allowed.");

		// Rejected rather than defaulted: a typo here silently points the server at a
		// different, empty set of tables, which looks exactly like losing every player.
		Position = (position?.Trim().ToLowerInvariant() ?? "") switch
		{
			"" or "before" => TablePrefixPosition.Before,
			"after" => TablePrefixPosition.After,
			_ => throw new InvalidOperationException(
				$"Database.TablePrefixPosition '{position}' is invalid - use 'before' or 'after'.")
		};

		Prefix = prefix;
	}
}

/// <summary>
/// Attaches the configured prefix to the <c>[Table]</c> name of this plugin's entities.
/// </summary>
internal sealed class PrefixedTableNameResolver : ITableNameResolver
{
	private static readonly DefaultTableNameResolver Default = new();

	public string ResolveTableName(Type type)
	{
		var name = Default.ResolveTableName(type);

		// Dommel's resolver hook is process-wide; never rename another plugin's entities.
		return type.Assembly == typeof(PrefixedTableNameResolver).Assembly
			? TableNames.Apply(name)
			: name;
	}
}
