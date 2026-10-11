using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace K4Ranks.Stats.Events;

// =========================================
// =           PLAYER DEATH HANDLER
// =========================================

/// <summary>
/// Handles all EventPlayerDeath processing in a single hook.
/// Includes: Kill, Death, Headshot, Assist, TeamKill, Suicide, Killstreak, Special Kills
/// </summary>
public sealed class PlayerDeathHandler(PluginConfig config, PointsConfig points, ModuleConfig modules, Func<PluginConfig> liveConfig, Func<IPlayer?, Plugin.PlayerDataService.SlotEntry?> resolve, Action<IPlayer, int, string, bool, string?> modifyPoints, Func<bool> canProcess)
{
	// =========================================
	// =           MAIN HANDLER
	// =========================================

	public HookResult OnPlayerDeath(EventPlayerDeath @event)
	{
		if (!canProcess())
			return HookResult.Continue;

		var attacker = @event.Accessor.GetPlayer("attacker");
		var victim = @event.UserIdPlayer;
		var assister = @event.Accessor.GetPlayer("assister");

		// IsValid re-resolves the controller and pawn each call; read it once per participant.
		var attackerValid = attacker?.IsValid == true;
		var victimValid = victim?.IsValid == true;
		var assisterValid = assister?.IsValid == true;

		// Bots never get PlayerData, so a loaded record also means "human".
		var victimEntry = victimValid ? resolve(victim) : null;
		var attackerData = attackerValid ? resolve(attacker)?.Data : null;
		var victimData = victimEntry?.Data;
		var assisterData = assisterValid ? resolve(assister)?.Data : null;

		var attackerController = attackerValid ? attacker!.Controller : null;
		var victimController = victimValid ? victim!.Controller : null;

		var weapon = @event.Weapon ?? "";
		var headshot = @event.Headshot;

		// ModifyPoints shows names only under these live settings, so build them only then.
		var livePoints = liveConfig().Points;
		var wantNames = livePoints.ShowPlayerNames && !livePoints.RoundEndSummary;

		// Determine kill type
		var isSuicide = !attackerValid || ReferenceEquals(attacker, victim);
		var isTeamKill = !isSuicide && !config.Rank.FFAMode && victimValid &&
			attackerController?.Team == victimController?.Team;
		var isBotKill = victimEntry?.IsBot ?? victim?.IsFakeClient ?? false;
		var isValidKill = !isSuicide && !isTeamKill && (!isBotKill || config.Rank.PointsForBots);

		// Read before the victim's deduction lands, so both sides scale from the same matchup.
		var matchupDelta = MatchupDelta(victimData, attackerData);

		// =========================================
		// =           VICTIM PROCESSING
		// =========================================

		if (victimData?.IsLoaded == true)
		{
			if (isSuicide)
			{
				// Suicide
				ProcessPoints(victim!, points.Suicide, "k4.reason.suicide");
			}
			else if (!isTeamKill)
			{
				// Normal death
				var multiplier = DynamicMultiplier(matchupDelta, livePoints.DynamicDeathPoints,
					livePoints.DynamicDeathPointsMin, livePoints.DynamicDeathPointsMax);
				var deathPoints = (int)Math.Round(points.Death * multiplier);
				var attackerName = wantNames ? attackerController?.PlayerName : null;

				ProcessPoints(victim!, deathPoints, "k4.reason.death", attackerName);
				victimData.Deaths++;

				if (modules.WeaponStatsEnabled && weapon.Length > 0)
					victimData.RecordWeaponDeath(weapon);
			}
		}

		// =========================================
		// =           ATTACKER PROCESSING
		// =========================================

		if (attackerData?.IsLoaded == true && !isSuicide)
		{
			var victimName = wantNames ? victim?.Controller?.PlayerName : null;

			if (isTeamKill)
			{
				// Team kill penalty
				ProcessPoints(attacker!, points.TeamKill, "k4.reason.teamkill", victimName);
			}
			else if (isValidKill)
			{
				// Normal kill
				var multiplier = DynamicMultiplier(matchupDelta, livePoints.DynamicKillPoints,
					livePoints.DynamicKillPointsMin, livePoints.DynamicKillPointsMax);
				var killPoints = (int)Math.Round(points.Kill * multiplier);

				ProcessPoints(attacker!, killPoints, "k4.reason.kill", victimName);
				attackerData.Kills++;

				if (modules.WeaponStatsEnabled && weapon.Length > 0)
					attackerData.RecordWeaponKill(weapon, headshot);

				// Headshot bonus
				if (headshot)
				{
					ProcessPoints(attacker!, points.Headshot, "k4.reason.headshot", victimName);
					attackerData.Headshots++;
				}

				// Special kill bonuses
				ProcessSpecialKills(@event, weapon, attacker!, victimName);

				// Killstreak
				ProcessKillstreak(attacker!, attackerData);
			}
		}

		// =========================================
		// =           ASSISTER PROCESSING
		// =========================================

		if (assisterData?.IsLoaded == true)
		{
			var isTeamAssist = !config.Rank.FFAMode && victimValid &&
				assister!.Controller?.Team == victimController?.Team;

			if (@event.AssistedFlash)
			{
				// Flash assist
				if (isTeamAssist)
					ProcessPoints(assister!, points.TeamAssistFlash, "k4.reason.team_assist_flash");
				else
					ProcessPoints(assister!, points.AssistFlash, "k4.reason.assist_flash");
			}
			else
			{
				// Normal assist
				if (isTeamAssist)
					ProcessPoints(assister!, points.TeamAssist, "k4.reason.team_assist");
				else
				{
					ProcessPoints(assister!, points.Assist, "k4.reason.assist");
					assisterData.Assists++;
				}
			}
		}

		return HookResult.Continue;
	}

	// =========================================
	// =           SPECIAL KILLS
	// =========================================

	private void ProcessSpecialKills(EventPlayerDeath @event, string weapon, IPlayer attacker, string? victimName)
	{
		// Early return if no special kill bonuses are configured
		if (points.NoScope == 0 && points.Thrusmoke == 0 && points.BlindKill == 0 &&
			points.Penetrated == 0 && points.LongDistanceKill == 0 && points.KnifeKill == 0 &&
			points.TaserKill == 0 && points.GrenadeKill == 0 && points.InfernoKill == 0 && points.ImpactKill == 0)
			return;

		// Config first: every event field read is a native call.

		// No-scope
		if (points.NoScope != 0 && @event.NoScope)
			ProcessPoints(attacker, points.NoScope, "k4.reason.noscope", victimName);

		// Through smoke
		if (points.Thrusmoke != 0 && @event.ThruSmoke)
			ProcessPoints(attacker, points.Thrusmoke, "k4.reason.thrusmoke", victimName);

		// Blind kill
		if (points.BlindKill != 0 && @event.AttackerBlind)
			ProcessPoints(attacker, points.BlindKill, "k4.reason.blind", victimName);

		// Wallbang
		if (points.Penetrated != 0 && @event.Penetrated > 0)
			ProcessPoints(attacker, points.Penetrated, "k4.reason.penetrated", victimName);

		// Long distance
		if (points.LongDistanceKill != 0 && @event.Distance >= points.LongDistance)
			ProcessPoints(attacker, points.LongDistanceKill, "k4.reason.longdistance", victimName);

		// Weapon type bonuses - only process if any weapon bonus is configured
		if (points.KnifeKill != 0 || points.TaserKill != 0 || points.GrenadeKill != 0 ||
			points.InfernoKill != 0 || points.ImpactKill != 0)
		{
			var weaponType = Plugin.WeaponCache.GetWeaponType(weapon);

			if (weaponType == CSWeaponType.WEAPONTYPE_KNIFE && points.KnifeKill != 0)
				ProcessPoints(attacker, points.KnifeKill, "k4.reason.knife", victimName);

			if (weaponType == CSWeaponType.WEAPONTYPE_TASER && points.TaserKill != 0)
				ProcessPoints(attacker, points.TaserKill, "k4.reason.taser", victimName);

			if (points.GrenadeKill != 0 && weapon.Contains("hegrenade", StringComparison.OrdinalIgnoreCase))
				ProcessPoints(attacker, points.GrenadeKill, "k4.reason.grenade", victimName);

			if (points.InfernoKill != 0 && (weapon.Contains("inferno", StringComparison.OrdinalIgnoreCase) ||
				weapon.Contains("molotov", StringComparison.OrdinalIgnoreCase) || weapon.Contains("incgrenade", StringComparison.OrdinalIgnoreCase)))
				ProcessPoints(attacker, points.InfernoKill, "k4.reason.inferno", victimName);

			if (points.ImpactKill != 0 && (weapon.Contains("flashbang", StringComparison.OrdinalIgnoreCase) ||
				weapon.Contains("smokegrenade", StringComparison.OrdinalIgnoreCase) || weapon.Contains("decoy", StringComparison.OrdinalIgnoreCase)))
				ProcessPoints(attacker, points.ImpactKill, "k4.reason.impact", victimName);
		}
	}

	// =========================================
	// =           KILLSTREAK
	// =========================================

	private void ProcessKillstreak(IPlayer attacker, PlayerData data)
	{
		// Early return if no killstreak bonuses are configured
		if (points.DoubleKill == 0 && points.TripleKill == 0 && points.Domination == 0 &&
			points.Rampage == 0 && points.MegaKill == 0 && points.Ownage == 0 &&
			points.UltraKill == 0 && points.KillingSpree == 0 && points.MonsterKill == 0 &&
			points.Unstoppable == 0 && points.GodLike == 0)
			return;

		var now = DateTime.UtcNow;

		if (points.SecondsBetweenKills > 0 && (now - data.LastKillTime).TotalSeconds > points.SecondsBetweenKills)
			data.Killstreak = 1;
		else
			data.Killstreak++;

		data.LastKillTime = now;

		var (_points, key) = data.Killstreak switch
		{
			2 => (points.DoubleKill, "k4.reason.doublekill"),
			3 => (points.TripleKill, "k4.reason.triplekill"),
			4 => (points.Domination, "k4.reason.domination"),
			5 => (points.Rampage, "k4.reason.rampage"),
			6 => (points.MegaKill, "k4.reason.megakill"),
			7 => (points.Ownage, "k4.reason.ownage"),
			8 => (points.UltraKill, "k4.reason.ultrakill"),
			9 => (points.KillingSpree, "k4.reason.killingspree"),
			10 => (points.MonsterKill, "k4.reason.monsterkill"),
			11 => (points.Unstoppable, "k4.reason.unstoppable"),
			>= 12 => (points.GodLike, "k4.reason.godlike"),
			_ => (0, "")
		};

		if (_points != 0 && !string.IsNullOrEmpty(key))
			ProcessPoints(attacker, _points, key);
	}

	// =========================================
	// =           HELPERS
	// =========================================

	private void ProcessPoints(IPlayer player, int points, string key, string? info = null)
	{
		if (points == 0)
			return;

		modifyPoints(player, points, key, true, info);
	}

	/// <summary>
	/// How much stronger the victim is than the attacker, in [-1, +1]; null when either side
	/// has no loaded record, since a bot is not a counterparty to compare against.
	/// </summary>
	private static double? MatchupDelta(PlayerData? victimData, PlayerData? attackerData)
	{
		if (victimData?.IsLoaded != true || attackerData?.IsLoaded != true)
			return null;

		// Dividing by the attacker alone explodes when they sit near zero - the exact
		// fresh-player case this exists for. Clamp covers points driven negative.
		var scale = Math.Max(Math.Max(victimData.Points, attackerData.Points), 1);
		return Math.Clamp((double)(victimData.Points - attackerData.Points) / scale, -1.0, 1.0);
	}

	/// <summary>
	/// Multiplier for one side of the kill. Pinned at 1.0 for an even matchup so the configured
	/// point value applies as-is; a stronger victim pulls toward <paramref name="max"/>.
	/// </summary>
	private static double DynamicMultiplier(double? delta, bool enabled, double min, double max)
	{
		if (!enabled || delta is not double d)
			return 1.0;

		return d >= 0 ? 1.0 + d * (max - 1.0) : 1.0 + d * (1.0 - min);
	}
}
