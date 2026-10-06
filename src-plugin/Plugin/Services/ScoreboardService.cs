using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace K4Ranks;

/// <summary>
/// Handles scoreboard rank display and reveal all functionality
/// </summary>
public sealed class ScoreboardService(Plugin plugin)
{
	/* ==================== Fields ==================== */

	private readonly ISwiftlyCore _core = Plugin.Core;
	private bool _isRunning;

	// Mirrors Scoreboard.UseRanks so the tick does no config read; refreshed on config change.
	private volatile bool _useRanks;
	private IDisposable? _configListener;

	/// <summary>
	/// Per-player virtual point overrides.
	/// When set, both the displayed rank icon and the displayed point value are
	/// derived from this virtual points figure rather than the player's real points.
	/// The actual <see cref="PlayerData.Points"/> value is never modified.
	/// </summary>
	private readonly ConcurrentDictionary<ulong, int> _pointOverrides = new();

	/// <summary>
	/// Cached scoreboard values computed on <c>round_prestart</c> (or immediately
	/// when an override is set). The tick handler replays them, through
	/// <see cref="_slotRanks"/>, every frame so the rank icon stays visible continuously.
	/// <c>Wins</c> is the player's real <see cref="PlayerData.GameWins"/> at cache
	/// time — CS2's revamped scoreboard appears to fold the wins count into the
	/// rank-icon derivation, so a stale/duplicate value (previously hardcoded to
	/// 10 for everyone) collapsed all players to the same displayed rank.
	/// </summary>
	private readonly ConcurrentDictionary<ulong, (int RankId, int DisplayPoints, int Wins)> _cachedRanks = new();

	private const int MaxSlots = 64;

	/// <summary>
	/// Slot-indexed view of <see cref="_cachedRanks"/> with the controller triple
	/// already resolved, so the tick does no SteamID, config or IsValid work.
	/// Also carries the last triple actually replicated to the client (the
	/// <c>Notified*</c> fields) — the change detector consulted by
	/// <see cref="ApplyCompetitiveRank"/> so the 64 Hz pass only fires notifiers
	/// when the intended value diverges from what the client has — either because
	/// the authoritative cache changed, or because the engine / scoreboard-refresh
	/// loop cleared the controller field between snapshots.
	/// Game thread only.
	/// </summary>
	private readonly SlotRank[] _slotRanks = new SlotRank[MaxSlots];

	// Bumped from any thread when _cachedRanks or the config changes; every slot
	// re-resolves on the next tick.
	private int _rankVersion;
	private int _tickCount;

	private struct SlotRank
	{
		public IPlayer? Player;
		public int Version;
		public bool HasRank;
		public byte RankType;
		public int RankValue;
		public int Wins;

		public bool HasNotified;
		public byte NotifiedRankType;
		public int NotifiedRankValue;
		public int NotifiedWins;
	}

	/* ==================== Rank Overrides ==================== */

	/// <summary>
	/// Virtually overrides the scoreboard-displayed rank and point value for a player.
	/// Both the rank icon and the Premier-mode point number shown in the CS2 scoreboard
	/// are derived from <paramref name="virtualPoints"/>. The player's actual
	/// <see cref="PlayerData.Points"/> is never modified.
	/// </summary>
	/// <param name="steamId">64-bit Steam ID of the target player.</param>
	/// <param name="virtualPoints">Virtual point value to display on the scoreboard.</param>
	public void SetPointOverride(ulong steamId, int virtualPoints) =>
		_pointOverrides[steamId] = virtualPoints;

	/// <summary>
	/// Removes a previously set virtual point override so the scoreboard reverts to
	/// displaying the rank computed from the player's real points.
	/// </summary>
	public void ClearPointOverride(ulong steamId) =>
		_pointOverrides.TryRemove(steamId, out _);

	/// <summary>
	/// Returns whether a virtual point override is active for the given player, and
	/// the override value if so.
	/// </summary>
	public bool TryGetPointOverride(ulong steamId, out int virtualPoints) =>
		_pointOverrides.TryGetValue(steamId, out virtualPoints);

	/// <summary>
	/// Removes the cached rank entry for a player. Call on disconnect to prevent
	/// stale data accumulating in <see cref="_cachedRanks"/>. The slot's notified
	/// mirror resets on its own when a reconnect brings a new player object, so a
	/// reconnect whose controller fields were reset by the engine is re-notified.
	/// </summary>
	public void RemoveCachedRank(ulong steamId)
	{
		_cachedRanks.TryRemove(steamId, out _);
		_pointOverrides.TryRemove(steamId, out _);
		Interlocked.Increment(ref _rankVersion);
	}

	/* ==================== Per-Player Refresh ==================== */

	/// <summary>
	/// Recalculates and caches the effective scoreboard rank for a single player,
	/// then applies it immediately. Use this after setting or clearing an override
	/// so the icon updates without waiting for the next <c>round_prestart</c>.
	/// </summary>
	public void UpdatePlayerScoreboard(IPlayer player)
	{
		if (!plugin.Config.CurrentValue.Scoreboard.UseRanks)
			return;

		// Bots never get a record, so loaded data also means "human".
		var data = plugin.PlayerData.GetPlayerData(player);
		if (data == null || !data.IsLoaded || !player.IsValid)
			return;

		var cfg = plugin.Config.CurrentValue.Scoreboard;
		CacheAndApplyScoreboardRank(player, data, cfg);
	}

	/* ==================== Start / Stop ==================== */

	public void Start()
	{
		if (_isRunning)
			return;

		_isRunning = true;

		_useRanks = plugin.Config.CurrentValue.Scoreboard.UseRanks;
		_configListener = plugin.Config.OnChange(OnConfigChanged);

		if (_useRanks)
			_core.Event.OnTick += ApplyAllCachedScoreboards;

		// StartRevealAllTimer self-guards on RevealAllInterval <= 0 and returns
		// without scheduling — per-connect broadcasts from OnPlayerActivate,
		// per-round broadcasts from OnRoundPrestart, and the hot-reload broadcast
		// cover the one-shot case, so ranks are always visible even when the
		// periodic timer is disabled.
		StartRevealAllTimer();
	}

	public void Stop()
	{
		if (!_isRunning)
			return;

		_isRunning = false;
		_core.Event.OnTick -= ApplyAllCachedScoreboards;

		_configListener?.Dispose();
		_configListener = null;
	}

	// Runs on the options reload thread: only volatile / Interlocked writes here.
	private void OnConfigChanged(PluginConfig config)
	{
		_useRanks = config.Scoreboard.UseRanks;

		// RankMode / CustomRank* are baked into the pre-resolved slot triples.
		Interlocked.Increment(ref _rankVersion);
	}

	/* ==================== Reveal All ==================== */

	private void StartRevealAllTimer()
	{
		var configuredInterval = plugin.Config.CurrentValue.Scoreboard.RevealAllInterval;

		// Honour config hot-reload: if the operator flips RevealAllInterval from
		// >0 to 0 at runtime, the recursive timer stops firing. Fresh connects
		// still get their per-player broadcast via OnPlayerActivate, so ranks
		// stay visible for new players. Existing players keep the last grant
		// they were sent (refreshed at round_prestart).
		if (configuredInterval <= 0f)
			return;

		var interval = Math.Max(configuredInterval, 1f);

		_core.Scheduler.DelayBySeconds(interval, () =>
		{
			if (_isRunning)
			{
				SendRevealAll();
				StartRevealAllTimer();
			}
		});
	}

	/// <summary>
	/// Broadcasts <c>CCSUsrMsg_ServerRankRevealAll</c> to every connected
	/// player, granting their client permission to render other players' rank
	/// icons on the scoreboard. Without this grant, CS2 hides ranks for anyone
	/// not in the viewing client's Steam friends list.
	/// <para>
	/// The client grant is time-limited and gets cleared on map load and other
	/// state resets, so this is called on plugin start, on every
	/// <c>OnPlayerActivate</c>, on <c>OnMapLoad</c>, on <c>OnRoundPrestart</c>
	/// (natural periodic refresh tied to gameplay rhythm), on hot-reload after
	/// existing players' data is loaded, and periodically from
	/// <see cref="StartRevealAllTimer"/> when the operator has configured a
	/// non-zero <c>RevealAllInterval</c>.
	/// </para>
	/// </summary>
	public void SendRevealAll()
	{
		if (!_isRunning)
			return;

		try
		{
			_core.NetMessage.Send<CCSUsrMsg_ServerRankRevealAll>(msg =>
			{
				msg.Recipients.AddAllPlayers();
			});
		}
		catch (Exception ex)
		{
			// Defensive: a future Valve protobuf schema change could make Send
			// throw. Log at Error level so operators notice, but don't crash the
			// caller (this runs from the reveal timer, OnPlayerActivate,
			// OnMapLoad, OnRoundPrestart, and the hot-reload path — an uncaught
			// exception in any of those breaks unrelated logic).
			Plugin.Core.Logger.LogError(ex, "Send<CCSUsrMsg_ServerRankRevealAll> threw");
		}
	}

	/* ==================== Scoreboard Updates ==================== */

	/// <summary>
	/// Recalculates and caches the effective rank for every loaded player.
	/// Called on <c>round_prestart</c> — rank values are computed once per round
	/// and then replayed every tick by <see cref="ApplyAllCachedScoreboards"/>.
	/// </summary>
	public void UpdateAllScoreboards()
	{
		if (!plugin.Config.CurrentValue.Scoreboard.UseRanks)
			return;

		var cfg = plugin.Config.CurrentValue.Scoreboard;

		foreach (var player in _core.PlayerManager.GetAllValidPlayers())
		{
			// Bots never get a record, so loaded data also means "human".
			var data = plugin.PlayerData.GetPlayerData(player);
			if (data == null || !data.IsLoaded)
				continue;

			CacheAndApplyScoreboardRank(player, data, cfg);
		}
	}

	/// <summary>
	/// Tick handler — replays the cached rank triple for every player, gated by
	/// the change detector in <see cref="ApplyCompetitiveRank"/>. In steady state
	/// (cache unchanged, controller fields intact) this does no I/O per player
	/// per tick beyond three field reads and a compare.
	/// <para>
	/// The detector fires the <c>*Updated()</c> notifiers when either the
	/// intended value differs from the last-notified triple (authoritative
	/// cache update landed — kill, round, admin command, override change) or
	/// the controller's current fields differ from the last-notified triple
	/// (client / engine cleared them between snapshots — the "all players
	/// show ?" recovery path). Notifiers thus fire on the order of once per
	/// real event per player, not 64 Hz.
	/// </para>
	/// <para>
	/// The previous implementation wrote the ref fields on every tick without
	/// firing notifiers, relying on the engine's snapshot system to pick up
	/// the writes. Valve's scoreboard-optimization patch tightened delta
	/// detection so silent writes are no longer replicated; the notifier IS
	/// the replication trigger, so we now have to fire it — but only when the
	/// value actually changed.
	/// </para>
	/// <para>
	/// Reads only the pre-resolved <see cref="_slotRanks"/>; a slot re-resolves when
	/// its player object or <see cref="_rankVersion"/> changes.
	/// </para>
	/// </summary>
	private void ApplyAllCachedScoreboards()
	{
		if (!_useRanks)
			return;

		var version = Volatile.Read(ref _rankVersion);
		var players = _core.PlayerManager;

		// SteamID can resolve after the last version bump (late auth), so rankless
		// slots also retry once every 64 ticks.
		var retryRankless = (++_tickCount & 63) == 0;

		for (var slot = 0; slot < MaxSlots; slot++)
		{
			ref var entry = ref _slotRanks[slot];
			var player = players.GetPlayer(slot);

			if (player == null)
			{
				if (entry.Player != null)
					entry = default;
				continue;
			}

			if (!ReferenceEquals(entry.Player, player) || entry.Version != version || (retryRankless && !entry.HasRank))
				ResolveSlotRank(ref entry, player, version);

			if (entry.HasRank)
				ApplyCompetitiveRank(player, ref entry);
		}
	}

	private void ResolveSlotRank(ref SlotRank entry, IPlayer player, int version)
	{
		// A new player object is a new connection: its client has seen nothing yet.
		if (!ReferenceEquals(entry.Player, player))
			entry = default;

		entry.Player = player;
		entry.Version = version;

		// Bots never get a cache entry; the slot cache holds their identity, so no native read.
		if (plugin.PlayerData.Resolve(player) is not { IsBot: false } identity
			|| !_cachedRanks.TryGetValue(identity.SteamId, out var cached))
		{
			entry.HasRank = false;
			return;
		}

		entry.HasRank = true;
		SetSlotTriple(ref entry, cached.RankId, cached.DisplayPoints, cached.Wins, plugin.Config.CurrentValue.Scoreboard);
	}

	private static void SetSlotTriple(ref SlotRank entry, int rankId, int displayPoints, int wins, ScoreboardSettings cfg)
	{
		(entry.RankType, entry.RankValue) = ResolveCompetitiveDisplay(
			cfg.RankMode, rankId, displayPoints, cfg.CustomRankMax, cfg.CustomRankBase, cfg.CustomRankMargin);
		entry.Wins = wins;
	}

	/// <summary>
	/// Computes the effective rank (real or virtual), stores it in the cache, and
	/// applies it to the player's controller immediately.
	/// When a virtual point override is active, both the rank icon and the
	/// Premier-mode point number are derived from that override value.
	/// </summary>
	private void CacheAndApplyScoreboardRank(IPlayer player, PlayerData data, ScoreboardSettings cfg)
	{
		int effectivePoints = TryGetPointOverride(data.SteamId64, out var virtualPoints)
			? virtualPoints
			: data.Points;

		int rankId = plugin.Ranks.GetRankId(effectivePoints);

		// CS2's Premier / Competitive scoreboard hides the rank icon behind a
		// "N wins needed" placeholder for players with fewer than 10 wins on
		// the CompetitiveWins field. Real DB-tracked match wins in
		// `data.GameWins` start at 0 and only accrue on match end, so a fresh
		// or low-activity player would otherwise see the placeholder instead
		// of the rank icon their real Points already earned.
		//
		// Floor the field at 10 so the icon always unlocks. Real wins above
		// 10 flow through unchanged, preserving per-player variation for the
		// wins-count column of the scoreboard. The plugin's own DB value in
		// `data.GameWins` is untouched — only the value written to the
		// controller schema field is bumped.
		int wins = Math.Max(10, data.GameWins);

		_cachedRanks[data.SteamId64] = (rankId, effectivePoints, wins);
		var version = Interlocked.Increment(ref _rankVersion);

		// Apply through the slot so the change detector below fires the *Updated()
		// notifiers exactly once for this authoritative write, and the tick need not re-resolve.
		var slot = player.PlayerID;
		if ((uint)slot >= MaxSlots || !ReferenceEquals(_core.PlayerManager.GetPlayer(slot), player))
			return;

		ref var entry = ref _slotRanks[slot];
		if (!ReferenceEquals(entry.Player, player))
			entry = default;

		entry.Player = player;
		entry.Version = version;
		entry.HasRank = true;
		SetSlotTriple(ref entry, rankId, effectivePoints, wins, cfg);
		ApplyCompetitiveRank(player, ref entry);
	}

	/// <summary>
	/// Resolves the mode-dependent controller display values. Pure helper, run when
	/// a rank is cached or a slot re-resolves, never per tick. Wins is not affected
	/// by mode and flows through untouched.
	/// </summary>
	private static (byte RankType, int RankValue) ResolveCompetitiveDisplay(
		int mode, int rankId, int currentPoints,
		int rankMax, int rankBase, int rankMargin)
	{
		return mode switch
		{
			1 => ((byte)11, currentPoints),                     // Premier - show points directly
			2 => ((byte)12, Math.Min(rankId, 18)),              // Competitive MM (ranks 1-18)
			3 => ((byte)7,  Math.Min(rankId, 18)),              // Wingman (ranks 1-18)
			4 => ((byte)10, Math.Min(rankId, 15)),              // Danger Zone (ranks 1-15)
			_ => ((byte)12, Math.Max(0, rankId > rankMax        // Custom mode (0)
				? rankBase + rankMax - rankMargin
				: rankBase + (rankId - rankMargin - 1))),
		};
	}

	/// <summary>
	/// Writes the slot's competitive display fields and fires the
	/// <c>*Updated()</c> notifiers — but only when the change detector says
	/// there is a real delta. The detector consults the slot's <c>Notified*</c>
	/// mirror and the controller's live field values to skip when neither has moved.
	/// <para>
	/// All call sites are on the main game thread (OnTick, round_prestart,
	/// chat commands, <c>PlayerDataService</c>'s <c>NextWorldUpdate</c>
	/// continuation, and SharedApi consumers per SwiftlyS2 convention), so no
	/// synchronization is required around <see cref="_slotRanks"/> and no
	/// scheduler round-trip is needed for the writes. The notifier IS the
	/// replication trigger under Valve's tightened scoreboard delta detection
	/// (the "FPS drop on tab open" optimization): writing the ref fields
	/// without a matching <c>*Updated()</c> no longer marks them as changed
	/// for the next snapshot, so notifiers have to fire on every real change.
	/// </para>
	/// </summary>
	private static void ApplyCompetitiveRank(IPlayer player, ref SlotRank entry)
	{
		// Not player.IsValid: that also demands a pawn and resolves the controller three times.
		var controller = player.Controller;
		if (controller is not { IsValid: true })
			return;

		var rankType = entry.RankType;
		var rankValue = entry.RankValue;
		var wins = entry.Wins;

		// Change detector — write + notify unless ALL of these hold:
		//   1. we have a prior notify record for this slot's player, AND
		//   2. our intended triple matches what we last notified, AND
		//   3. the controller's live fields still match what we last notified
		//      (they don't in the "all players show ?" recovery path, where
		//      Valve's scoreboard-refresh loop clears the fields between
		//      snapshots — detect the drift here and re-notify).
		// If all three hold, the client already has the right values and re-
		// firing the *Updated() notifiers 64 Hz just spams the network layer.
		if (entry.HasNotified
			&& entry.NotifiedRankType == rankType
			&& entry.NotifiedRankValue == rankValue
			&& entry.NotifiedWins == wins
			&& controller.CompetitiveRanking == rankValue
			&& controller.CompetitiveRankType == rankType
			&& controller.CompetitiveWins == wins)
		{
			return;
		}

		controller.CompetitiveRankType = rankType;
		controller.CompetitiveRanking = rankValue;
		controller.CompetitiveWins = wins;

		controller.CompetitiveRankTypeUpdated();
		controller.CompetitiveRankingUpdated();
		controller.CompetitiveWinsUpdated();

		entry.HasNotified = true;
		entry.NotifiedRankType = rankType;
		entry.NotifiedRankValue = rankValue;
		entry.NotifiedWins = wins;
	}
}

