using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Ranks;

public sealed partial class Plugin
{
	/// <summary>
	/// Static weapon cache with lazy initialization at map load.
	/// Caches weapon data from game to avoid hardcoded lists.
	/// </summary>
	public static class WeaponCache
	{
		/* ==================== Fields ==================== */

		private const string ClassnamePrefix = "weapon_";

		// Longest unprefixed name whose lookup key is built on the stack.
		private const int MaxStackName = 64;

		private static Dictionary<CSWeaponType, List<WeaponInfo>>? _weaponsByType;
		private static Dictionary<ItemDefinitionIndex, WeaponInfo>? _weaponsByIndex;
		private static Dictionary<string, WeaponInfo>? _weaponsByClassname;

		/* ==================== Properties ==================== */

		private static bool IsInitialized => _weaponsByIndex?.Count > 0;

		/* ==================== Types ==================== */

		public sealed class WeaponInfo
		{
			public required ItemDefinitionIndex Index { get; init; }
			public required string Classname { get; init; }
			public required string DisplayName { get; init; }
			public required CSWeaponType Type { get; init; }
			public required bool IsPrimary { get; init; }
			public required bool IsSecondary { get; init; }
		}

		/* ==================== Initialization ==================== */

		public static void Initialize()
		{
			if (IsInitialized)
				return;

			// Built in locals and published at the end: menus and stat loads may read this
			// cache from other threads and must never see a half-filled map.
			var byType = new Dictionary<CSWeaponType, List<WeaponInfo>>();
			var byIndex = new Dictionary<ItemDefinitionIndex, WeaponInfo>();
			var byClassname = new Dictionary<string, WeaponInfo>(StringComparer.OrdinalIgnoreCase);

			foreach (var type in Enum.GetValues<CSWeaponType>())
				byType[type] = [];

			foreach (var index in Enum.GetValues<ItemDefinitionIndex>())
				ProcessWeaponIndex(index, byType, byIndex, byClassname);

			_weaponsByType = byType;
			_weaponsByClassname = byClassname;

			// Last: IsInitialized keys off this map.
			Volatile.Write(ref _weaponsByIndex, byIndex);

			if (byIndex.Count > 0)
				Core.Logger.LogInformation("WeaponCache initialized: {Count} weapons cached", byIndex.Count);
		}

		private static void ProcessWeaponIndex(
			ItemDefinitionIndex index,
			Dictionary<CSWeaponType, List<WeaponInfo>> byType,
			Dictionary<ItemDefinitionIndex, WeaponInfo> byIndex,
			Dictionary<string, WeaponInfo> byClassname)
		{
			var vdata = Core.Helpers.GetWeaponCSDataFromKey(index);
			if (vdata == null)
				return;

			var enumName = index.ToString();
			var classname = $"{ClassnamePrefix}{ToSnakeCase(enumName)}";
			var type = vdata.WeaponType;

			var isPrimary = type is
				CSWeaponType.WEAPONTYPE_RIFLE or
				CSWeaponType.WEAPONTYPE_SNIPER_RIFLE or
				CSWeaponType.WEAPONTYPE_SHOTGUN or
				CSWeaponType.WEAPONTYPE_SUBMACHINEGUN or
				CSWeaponType.WEAPONTYPE_MACHINEGUN;

			var info = new WeaponInfo
			{
				Index = index,
				Classname = classname,
				DisplayName = enumName,
				Type = type,
				IsPrimary = isPrimary,
				IsSecondary = type == CSWeaponType.WEAPONTYPE_PISTOL
			};

			byIndex[index] = info;
			byClassname[classname] = info;
			byType[type].Add(info);
		}

		public static void Reset()
		{
			_weaponsByType = null;
			_weaponsByIndex = null;
			_weaponsByClassname = null;
		}

		/* ==================== Lookup Methods ==================== */

		public static WeaponInfo? GetByIndex(ItemDefinitionIndex index)
		{
			EnsureInitialized();
			return _weaponsByIndex?.TryGetValue(index, out var info) == true ? info : null;
		}

		public static WeaponInfo? GetByClassname(string? classname)
		{
			if (string.IsNullOrEmpty(classname))
				return null;

			EnsureInitialized();

			var map = _weaponsByClassname;
			if (map == null)
				return null;

			// The map ignores case, so no lowercase copy is needed.
			WeaponInfo? info;
			if (classname.StartsWith(ClassnamePrefix, StringComparison.OrdinalIgnoreCase))
				return map.TryGetValue(classname, out info) ? info : null;

			if (classname.Length > MaxStackName)
				return map.TryGetValue(ClassnamePrefix + classname, out info) ? info : null;

			// Event weapon names carry no prefix ("ak47"); build "weapon_ak47" on the stack.
			Span<char> key = stackalloc char[ClassnamePrefix.Length + classname.Length];
			ClassnamePrefix.CopyTo(key);
			classname.CopyTo(key[ClassnamePrefix.Length..]);
			return map.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(key, out info) ? info : null;
		}

		public static IReadOnlyList<WeaponInfo> GetByType(CSWeaponType type)
		{
			EnsureInitialized();
			return _weaponsByType?.TryGetValue(type, out var list) == true ? list : [];
		}

		public static IReadOnlyCollection<WeaponInfo> GetAll()
		{
			EnsureInitialized();
			return _weaponsByIndex?.Values ?? (IReadOnlyCollection<WeaponInfo>)[];
		}

		public static IEnumerable<WeaponInfo> GetAllPrimaries()
		{
			EnsureInitialized();
			return _weaponsByIndex?.Values.Where(w => w.IsPrimary) ?? [];
		}

		public static IEnumerable<WeaponInfo> GetAllSecondaries()
		{
			EnsureInitialized();
			return _weaponsByIndex?.Values.Where(w => w.IsSecondary) ?? [];
		}

		/* ==================== Parsing Helpers ==================== */

		public static ItemDefinitionIndex? ParseWeapon(string? weaponName)
		{
			var info = GetByClassname(weaponName);
			return info?.Index;
		}

		public static CSWeaponType GetWeaponType(string? weaponName)
		{
			var info = GetByClassname(weaponName);
			return info?.Type ?? CSWeaponType.WEAPONTYPE_UNKNOWN;
		}

		/* ==================== Private Helpers ==================== */

		private static string ToSnakeCase(string name)
		{
			var result = new System.Text.StringBuilder();

			for (int i = 0; i < name.Length; i++)
			{
				var c = name[i];
				if (char.IsUpper(c) && i > 0)
					result.Append('_');
				result.Append(char.ToLowerInvariant(c));
			}

			return result.ToString();
		}

		private static void EnsureInitialized()
		{
			if (!IsInitialized)
			{
				Core.Logger.LogWarning("WeaponCache accessed before initialization, initializing now");
				Initialize();
			}
		}
	}
}
