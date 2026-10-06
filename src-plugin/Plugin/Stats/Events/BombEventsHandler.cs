using System.Diagnostics.CodeAnalysis;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace K4Ranks.Stats.Events;

// =========================================
// =           BOMB EVENTS HANDLER
// =========================================

/// <summary>
/// Handles all bomb-related events in separate hooks.
/// Includes: Plant, Defuse, Explode, Pickup, Drop
/// </summary>
public sealed class BombEventsHandler(ISwiftlyCore core, PointsConfig points, Func<IPlayer, PlayerData?> getPlayerData, Action<IPlayer, int, string, bool, string?> modifyPoints, Func<bool> canProcess)
{
	// =========================================
	// =           BOMB PLANTED
	// =========================================

	public HookResult OnBombPlanted(EventBombPlanted @event)
	{
		if (!canProcess() || points.BombPlant == 0)
			return HookResult.Continue;

		var player = @event.UserIdPlayer;
		if (!IsValidLoaded(player))
			return HookResult.Continue;

		modifyPoints(player, points.BombPlant, "k4.reason.bombplant", true, null);
		return HookResult.Continue;
	}

	// =========================================
	// =           BOMB DEFUSED
	// =========================================

	public HookResult OnBombDefused(EventBombDefused @event)
	{
		if (!canProcess())
			return HookResult.Continue;

		var defuser = @event.UserIdPlayer;

		// Award defuser
		if (IsValidLoaded(defuser) && points.BombDefuse != 0)
			modifyPoints(defuser, points.BombDefuse, "k4.reason.bombdefuse", true, null);

		// Award other CTs
		if (points.BombDefuseOthers == 0)
			return HookResult.Continue;

		foreach (var player in core.PlayerManager.GetAllValidPlayers())
		{
			if (ReferenceEquals(player, defuser) || !IsValidLoaded(player))
				continue;

			// Team 3 = CounterTerrorist
			var team = (int)(player.Controller?.Team ?? Team.None);
			if (team != 3)
				continue;

			modifyPoints(player, points.BombDefuseOthers, "k4.reason.bombdefuse_others", true, null);
		}

		return HookResult.Continue;
	}

	// =========================================
	// =           BOMB EXPLODED
	// =========================================

	public HookResult OnBombExploded(EventBombExploded @event)
	{
		if (!canProcess() || points.BombExploded == 0)
			return HookResult.Continue;

		var player = @event.UserIdPlayer;
		if (!IsValidLoaded(player))
			return HookResult.Continue;

		modifyPoints(player, points.BombExploded, "k4.reason.bombexploded", true, null);
		return HookResult.Continue;
	}

	// =========================================
	// =           BOMB PICKUP
	// =========================================

	public HookResult OnBombPickup(EventBombPickup @event)
	{
		if (!canProcess() || points.BombPickup == 0)
			return HookResult.Continue;

		var player = @event.UserIdPlayer;
		if (!IsValidLoaded(player))
			return HookResult.Continue;

		modifyPoints(player, points.BombPickup, "k4.reason.bombpickup", true, null);
		return HookResult.Continue;
	}

	// =========================================
	// =           BOMB DROPPED
	// =========================================

	public HookResult OnBombDropped(EventBombDropped @event)
	{
		if (!canProcess() || points.BombDrop == 0)
			return HookResult.Continue;

		var player = @event.UserIdPlayer;
		if (!IsValidLoaded(player))
			return HookResult.Continue;

		modifyPoints(player, points.BombDrop, "k4.reason.bombdrop", true, null);
		return HookResult.Continue;
	}

	// =========================================
	// =           HELPER
	// =========================================

	private bool IsValidLoaded([NotNullWhen(true)] IPlayer? player)
	{
		// Bots never get a record, so loaded data also means "human".
		return player is not null && getPlayerData(player)?.IsLoaded == true && player.IsValid;
	}
}
