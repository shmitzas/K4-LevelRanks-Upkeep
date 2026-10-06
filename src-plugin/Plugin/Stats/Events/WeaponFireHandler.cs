using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace K4Ranks.Stats.Events;

// =========================================
// =           WEAPON FIRE HANDLER
// =========================================

/// <summary>
/// Handles all EventWeaponFire processing in a single hook.
/// Tracks: Shots fired per weapon
/// </summary>
public sealed class WeaponFireHandler(ModuleConfig modules, Func<IPlayer?, Plugin.PlayerDataService.SlotEntry?> resolve, Func<bool> canProcess)
{

	// =========================================
	// =           MAIN HANDLER
	// =========================================

	public HookResult OnWeaponFire(EventWeaponFire @event)
	{
		if (!canProcess())
			return HookResult.Continue;

		// Hottest event in the plugin. Loaded data implies a connected human, and a shot
		// implies a live pawn, so no native IsValid / IsFakeClient reads are needed.
		var data = resolve(@event.UserIdPlayer)?.Data;
		if (data == null || !data.IsLoaded)
			return HookResult.Continue;

		var weapon = @event.Weapon;
		if (string.IsNullOrEmpty(weapon))
			return HookResult.Continue;

		// =========================================
		// =           RECORD STATS
		// =========================================

		// Global shot stats (always tracked)
		data.RecordGlobalShot();

		// Weapon-specific shot stats
		if (modules.WeaponStatsEnabled)
			data.RecordWeaponShot(weapon);

		return HookResult.Continue;
	}
}
