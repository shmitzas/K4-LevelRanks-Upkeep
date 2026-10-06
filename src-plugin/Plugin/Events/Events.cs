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

	private const float SaveIntervalSeconds = 60f;
	private CancellationTokenSource? _saveTimerCts;

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

		StartSaveTimer();
	}

	private void InitializeEventHandlers()
	{
		_playerDeathHandler = new PlayerDeathHandler(
			Config.CurrentValue, Points.CurrentValue, Modules.CurrentValue,
			() => Config.CurrentValue,
			PlayerData.Resolve,
			PlayerData.ModifyPoints,
			ShouldProcessPoints
		);

		_playerHurtHandler = new PlayerHurtHandler(
			Config.CurrentValue, Modules.CurrentValue,
			PlayerData.Resolve,
			ShouldProcessPoints
		);

		_weaponFireHandler = new WeaponFireHandler(
			Modules.CurrentValue,
			PlayerData.Resolve,
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
		Core.GameEvent.HookPost<EventCsWinPanelMatch>(OnWinPanelMatch);
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

		// No IsValid gate: it needs a pawn, which a just-activated player may not have yet.
		if (player == null || player.IsFakeClient)
			return HookResult.Continue;

		// Broadcast RevealAll on every real connect so the newly-joined client
		// (and every other connected client) has a fresh grant to render ranks
		// on the scoreboard. This runs regardless of PlayerData load state
		// because the reveal grant is not per-player-data — it's per-client
		// permission — and cheap to re-issue. Covers the case where the
		// operator sets Scoreboard.RevealAllInterval = 0 and relies solely on
		// per-connect broadcasts.
		Scoreboard.SendRevealAll();

		// Idempotent: a repeated activate never reloads over a live record.
		PlayerData.EnsureLoaded(player);
		return HookResult.Continue;
	}

	private void OnClientDisconnected(IOnClientDisconnectedEvent @event)
	{
		// No IsValid gate: the pawn is often gone by now (spectator, dead), which used to skip the save.
		var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
		if (PlayerData.Resolve(player) is not { IsBot: false, SteamId: not 0 and var steamId })
			return;

		PlayerData.MarkDeparted(steamId);
		Scoreboard.RemoveCachedRank(steamId);
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

		// A round boundary is a natural save point; the timer covers long rounds.
		PlayerData.RequestSave();

		return HookResult.Continue;
	}

	private HookResult OnWinPanelMatch(EventCsWinPanelMatch @event)
	{
		// A map change may follow immediately; don't wait for the timer.
		PlayerData.RequestSave();

		return HookResult.Continue;
	}

	private void StartSaveTimer()
	{
		// Jittered first tick so servers restarted together don't save in lockstep.
		var firstDelay = SaveIntervalSeconds * (0.5f + Random.Shared.NextSingle());

		_saveTimerCts = Core.Scheduler.DelayAndRepeatBySeconds(firstDelay, SaveIntervalSeconds, () =>
		{
			PlayerData.LoadMissingPlayers();
			PlayerData.RequestSave();
		});
	}

	private void StopSaveTimer()
	{
		_saveTimerCts?.Cancel();
		_saveTimerCts = null;
	}

	private void ProcessRoundEndForPlayers()
	{
		var points = Config.CurrentValue.Points;
		if (!points.KillstreakResetOnRoundEnd && !points.RoundEndSummary)
			return;

		foreach (var player in Core.PlayerManager.GetAllValidPlayers())
		{
			// GetAllValidPlayers already filters IsValid, and bots never get a record.
			var data = PlayerData.GetPlayerData(player);
			if (data == null || !data.IsLoaded)
				continue;

			if (points.KillstreakResetOnRoundEnd)
				data.ResetKillstreak();

			if (points.RoundEndSummary)
				PlayerData.ShowRoundSummary(player);
		}
	}

	/* ==================== Helpers ==================== */

	// weapon_fire / player_hurt / player_death each ask this, dozens of times a second in
	// a gunfight. Warmup state and the human count can't meaningfully move inside a tick.
	private const long PointsGateWindowMs = 15;
	private long _pointsGateCheckedAt = -PointsGateWindowMs;
	private bool _pointsGateOpen;

	private bool ShouldProcessPoints()
	{
		var now = Environment.TickCount64;
		if (now - _pointsGateCheckedAt < PointsGateWindowMs)
			return _pointsGateOpen;

		var rank = Config.CurrentValue.Rank;
		_pointsGateOpen = (rank.WarmupPoints || !IsWarmup) && PlayerData.CountValidHumans() >= rank.MinPlayers;
		_pointsGateCheckedAt = now;
		return _pointsGateOpen;
	}

	private void CheckPlaytimeRewards()
	{
		if (ShouldProcessPoints())
			_playtimeReward?.CheckAndReward();
	}
}
