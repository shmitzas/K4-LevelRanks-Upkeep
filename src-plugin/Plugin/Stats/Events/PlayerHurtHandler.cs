using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace K4Ranks.Stats.Events;

// =========================================
// =           PLAYER HURT HANDLER
// =========================================

/// <summary>
/// Handles all EventPlayerHurt processing in a single hook.
/// Tracks: Shots hit, damage dealt, hitbox distribution
/// </summary>
public sealed class PlayerHurtHandler(PluginConfig config, ModuleConfig modules, Func<IPlayer?, Plugin.PlayerDataService.SlotEntry?> resolve, Func<bool> canProcess)
{

	// =========================================
	// =           MAIN HANDLER
	// =========================================

	public HookResult OnPlayerHurt(EventPlayerHurt @event)
	{
		if (!canProcess())
			return HookResult.Continue;

		// Loaded data implies a human attacker, so it replaces the IsFakeClient check.
		var attacker = @event.Accessor.GetPlayer("attacker");
		var attackerData = resolve(attacker)?.Data;
		if (attackerData == null || !attackerData.IsLoaded || !attacker!.IsValid)
			return HookResult.Continue;

		var victim = @event.UserIdPlayer;
		if (victim == null || ReferenceEquals(attacker, victim) || !victim.IsValid)
			return HookResult.Continue;

		// Skip bot damage if configured
		if (!config.Rank.PointsForBots && (resolve(victim)?.IsBot ?? victim.IsFakeClient))
			return HookResult.Continue;

		// Skip team damage (unless FFA)
		if (!config.Rank.FFAMode && attacker.Controller?.Team == victim.Controller?.Team)
			return HookResult.Continue;

		var healthDamage = @event.ActualDmgHealth;
		var armorDamage = @event.ActualDmgArmor;

		if (healthDamage <= 0 && armorDamage <= 0)
			return HookResult.Continue;

		// =========================================
		// =           RECORD STATS
		// =========================================

		// Global hit stats (always tracked)
		attackerData.RecordGlobalHit(healthDamage);

		// Weapon-specific hit stats
		if (modules.WeaponStatsEnabled)
		{
			var weapon = @event.Weapon;
			if (!string.IsNullOrEmpty(weapon))
				attackerData.RecordWeaponHit(weapon, healthDamage);
		}

		// Hitbox data
		if (modules.HitStatsEnabled)
		{
			var hitgroup = (int)@event.ActualHitGroup;
			attackerData.HitData.RecordHit(hitgroup, healthDamage, armorDamage);
		}

		return HookResult.Continue;
	}
}
