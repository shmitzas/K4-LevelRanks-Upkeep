using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;
using K4Ranks.Stats.Events;
using K4Ranks.Stats.Playtime;

namespace K4Ranks;

public sealed partial class Plugin
{
	/* ==================== Event Handlers ==================== */

	private PlayerDeathHandler? _playerDeathHandler;
	private PlayerHurtHandler? _playerHurtHandler;
	private WeaponFireHandler? _weaponFireHandler;
	private BombEventsHandler? _bombEventsHandler;
	private HostageEventsHandler? _hostageEventsHandler;
	private RoundEventsHandler? _roundEventsHandler;
	private PlaytimeReward? _playtimeReward;

	/* ==================== Event Registration ==================== */

	private void RegisterEvents()
	{
		Ranks.LoadRanks();

		InitializeEventHandlers();
		InitializePlaytimeReward();

		RegisterMapEvents();
		RegisterPlayerEvents();
		RegisterStatEvents();
		RegisterRoundEvents();
	}

	private void InitializeEventHandlers()
	{
		_playerDeathHandler = new PlayerDeathHandler(
			Config.CurrentValue, Points.CurrentValue, Modules.CurrentValue,
			PlayerData.GetPlayerData,
			PlayerData.ModifyPoints,
			ShouldProcessPoints
		);

		_playerHurtHandler = new PlayerHurtHandler(
			Config.CurrentValue, Modules.CurrentValue,
			PlayerData.GetPlayerData,
			ShouldProcessPoints
		);

		_weaponFireHandler = new WeaponFireHandler(
			Modules.CurrentValue,
			PlayerData.GetPlayerData,
			ShouldProcessPoints
		);

		_bombEventsHandler = new BombEventsHandler(
			Core, Points.CurrentValue,
			PlayerData.GetPlayerData,
			PlayerData.ModifyPoints,
			ShouldProcessPoints
		);

		_hostageEventsHandler = new HostageEventsHandler(
			Core, Points.CurrentValue,
			PlayerData.GetPlayerData,
			PlayerData.ModifyPoints,
			ShouldProcessPoints
		);

		_roundEventsHandler = new RoundEventsHandler(
			Core, Points.CurrentValue,
			PlayerData.GetPlayerData,
			PlayerData.ModifyPoints,
			ShouldProcessPoints
		);
	}

	private void InitializePlaytimeReward()
	{
		_playtimeReward = new PlaytimeReward(
			Core,
			Points.CurrentValue,
			PlayerData.GetPlayerData,
			PlayerData.ModifyPoints
		);

		if (Points.CurrentValue.PlaytimePoints > 0 && Points.CurrentValue.PlaytimeMinutes > 0)
			Core.Scheduler.RepeatBySeconds(30f, CheckPlaytimeRewards);
	}

	private void RegisterMapEvents()
	{
		Core.Event.OnMapLoad += OnMapLoad;
	}

	private void RegisterPlayerEvents()
	{
		Core.GameEvent.HookPost<EventPlayerActivate>(OnPlayerActivate);
		Core.Event.OnClientDisconnected += OnClientDisconnected;
	}

	private void RegisterStatEvents()
	{
		// Combat events
		Core.GameEvent.HookPost<EventPlayerDeath>(@event => _playerDeathHandler!.OnPlayerDeath(@event));
		Core.GameEvent.HookPost<EventPlayerHurt>(@event => _playerHurtHandler!.OnPlayerHurt(@event));
		Core.GameEvent.HookPost<EventWeaponFire>(@event => _weaponFireHandler!.OnWeaponFire(@event));

		// Bomb events
		Core.GameEvent.HookPost<EventBombPlanted>(@event => _bombEventsHandler!.OnBombPlanted(@event));
		Core.GameEvent.HookPost<EventBombDefused>(@event => _bombEventsHandler!.OnBombDefused(@event));
		Core.GameEvent.HookPost<EventBombExploded>(@event => _bombEventsHandler!.OnBombExploded(@event));
		Core.GameEvent.HookPost<EventBombPickup>(@event => _bombEventsHandler!.OnBombPickup(@event));
		Core.GameEvent.HookPost<EventBombDropped>(@event => _bombEventsHandler!.OnBombDropped(@event));

		// Hostage events
		Core.GameEvent.HookPost<EventHostageRescued>(@event => _hostageEventsHandler!.OnHostageRescued(@event));
		Core.GameEvent.HookPost<EventHostageRescuedAll>(@event => _hostageEventsHandler!.OnHostageRescuedAll(@event));
		Core.GameEvent.HookPost<EventHostageHurt>(@event => _hostageEventsHandler!.OnHostageHurt(@event));
		Core.GameEvent.HookPost<EventHostageKilled>(@event => _hostageEventsHandler!.OnHostageKilled(@event));

		// Round events (for points)
		Core.GameEvent.HookPost<EventRoundMvp>(@event => _roundEventsHandler!.OnRoundMvp(@event));
	}

	private void RegisterRoundEvents()
	{
		Core.GameEvent.HookPost<EventRoundPrestart>(OnRoundPrestart);
		Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart);
		Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd);
	}

	/* ==================== Map Events ==================== */

	private void OnMapLoad(IOnMapLoadEvent @event)
	{
		Core.Scheduler.DelayBySeconds(0.1f, WeaponCache.Initialize);

		// Existing players stay connected across a map change but their client-
		// side scoreboard state gets reset by the new-map load, dropping the
		// RevealAll grant. EventPlayerActivate may or may not re-fire for the
		// existing cohort on map change, and even when it does, the send often
		// lands too early — before the client's fresh scoreboard is ready to
		// honor the grant. Re-broadcast on a small delay so it lands after the
		// client has settled on the new map. One-shot per map change; not
		// periodic.
		Core.Scheduler.DelayBySeconds(1.0f, () => Scoreboard.SendRevealAll());
	}

	/* ==================== Player Events ==================== */

	private HookResult OnPlayerActivate(EventPlayerActivate @event)
	{
		var player = Core.PlayerManager.GetPlayer(@event.UserId);

		if (player == null || !player.IsValid || player.IsFakeClient)
			return HookResult.Continue;

		// Broadcast RevealAll on every real connect so the newly-joined client
		// (and every other connected client) has a fresh grant to render ranks
		// on the scoreboard. This runs regardless of PlayerData load state
		// because the reveal grant is not per-player-data — it's per-client
		// permission — and cheap to re-issue. Covers the case where the
		// operator sets Scoreboard.RevealAllInterval = 0 and relies solely on
		// per-connect broadcasts.
		Scoreboard.SendRevealAll();

		// EventPlayerActivate can fire more than once per session (full updates,
		// level transitions). Skip if the player is already loaded — re-loading
		// would overwrite the in-memory record with the DB row and silently
		// discard any points earned since the original load.
		if (PlayerData.IsPlayerLoaded(player.SteamID))
			return HookResult.Continue;

		Task.Run(() => PlayerData.LoadPlayerDataAsync(player));
		return HookResult.Continue;
	}

	private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
	{
		var player = Core.PlayerManager.GetPlayer(@event.PlayerId);

		if (player == null || !player.IsValid || player.IsFakeClient)
			return;

		var steamId = player.SteamID; // Capture before Task.Run to avoid ObjectDisposedException

		Task.Run(async () =>
		{
			await PlayerData.SavePlayerDataAsync(steamId);
			PlayerData.RemovePlayer(steamId);
			Scoreboard.RemoveCachedRank(steamId);
		});
	}

	/* ==================== Round Events ==================== */

	private HookResult OnRoundPrestart(EventRoundPrestart @event)
	{
		Scoreboard.UpdateAllScoreboards();

		// The CS2 client's RevealAll grant has a time-limited lifetime — after
		// a few minutes without a refresh, the client stops rendering other
		// players' rank icons for anyone not in its Steam friends list.
		// Piggybacking on round_prestart gives a natural refresh cadence tied
		// to gameplay rhythm (~once per round, roughly every 1-3 min in normal
		// play) instead of a wall-clock timer. Combined with the per-connect,
		// per-map-load, and hot-reload broadcasts, this closes the last gap:
		// ranks stay visible mid-session even with the periodic
		// RevealAllInterval timer disabled.
		Scoreboard.SendRevealAll();

		return HookResult.Continue;
	}

	private HookResult OnRoundStart(EventRoundStart @event)
	{
		foreach (var data in PlayerData.GetAllLoadedPlayers())
			data.ResetRoundData();

		return HookResult.Continue;
	}

	private HookResult OnRoundEnd(EventRoundEnd @event)
	{
		// Process round win/lose points
		_roundEventsHandler?.OnRoundEndPoints(@event);

		// Process round end for players (summary, killstreak reset)
		ProcessRoundEndForPlayers();

		// Save all player data
		Task.Run(() => PlayerData.SaveAllPlayersAsync());

		return HookResult.Continue;
	}

	private void ProcessRoundEndForPlayers()
	{
		foreach (var player in Core.PlayerManager.GetAllValidPlayers())
		{
			if (!player.IsValid || player.IsFakeClient)
				continue;

			var data = PlayerData.GetPlayerData(player);
			if (data == null || !data.IsLoaded)
				continue;

			if (Config.CurrentValue.Points.KillstreakResetOnRoundEnd)
				data.ResetKillstreak();

			if (Config.CurrentValue.Points.RoundEndSummary)
				PlayerData.ShowRoundSummary(player);
		}
	}

	/* ==================== Helpers ==================== */

	private bool ShouldProcessPoints()
	{
		if (IsWarmup && !Config.CurrentValue.Rank.WarmupPoints)
			return false;

		var playerCount = Core.PlayerManager
			.GetAllValidPlayers()
			.Count(p => p.IsValid && !p.IsFakeClient);

		return playerCount >= Config.CurrentValue.Rank.MinPlayers;
	}

	private void CheckPlaytimeRewards()
	{
		if (ShouldProcessPoints())
			_playtimeReward?.CheckAndReward();
	}
}
