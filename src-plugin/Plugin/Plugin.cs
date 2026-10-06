using K4RanksSharedApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;

namespace K4Ranks;

[PluginMetadata(
	Id = "k4.levelranks",
	Version = "1.4.0",
	Name = "K4 - Level Ranks",
	Author = "K4ryuu, Shmitz",
	Description = "Experience-based ranking system with configurable ranks, detailed player statistics, weapon tracking, and hit analysis for CS2."
)]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
	/* ==================== Static Access ==================== */

	public static new ISwiftlyCore Core { get; private set; } = null!;

	/* ==================== Configurations ==================== */

	internal IOptionsMonitor<PluginConfig> Config { get; private set; } = null!;
	internal IOptionsMonitor<PointsConfig> Points { get; private set; } = null!;
	internal IOptionsMonitor<RanksConfig> RanksConfig { get; private set; } = null!;
	internal IOptionsMonitor<CommandsConfig> Commands { get; private set; } = null!;
	internal IOptionsMonitor<ModuleConfig> Modules { get; private set; } = null!;

	/* ==================== Services ==================== */

	internal RankService Ranks { get; private set; } = null!;
	internal DatabaseService Database { get; private set; } = null!;
	internal PlayerDataService PlayerData { get; private set; } = null!;
	internal ScoreboardService Scoreboard { get; private set; } = null!;

	/* ==================== Game Rules ==================== */

	internal bool IsWarmup => Core.EntitySystem.GetGameRules()?.WarmupPeriod == true;

	/* ==================== Plugin Lifecycle ==================== */

	public override void Load(bool hotReload)
	{
		Core = base.Core;

		InitializeConfigs();
		InitializeServices();
		InitializeDatabase();
		PlayerData.StartWriter();

		RegisterEvents();
		RegisterCommands();

		Scoreboard.Start();

		if (hotReload)
			HandleHotReload();
	}

	private void InitializeServices()
	{
		Database = new DatabaseService(
			Config.CurrentValue.Database.Connection,
			Config.CurrentValue.Database.TablePrefix,
			Config.CurrentValue.Database.TablePrefixPosition,
			Config.CurrentValue.Database.PurgeDays,
			Modules.CurrentValue
		);

		Ranks = new RankService(RanksConfig.CurrentValue);
		PlayerData = new PlayerDataService(this);
		Scoreboard = new ScoreboardService(this);
	}

	private void InitializeDatabase()
	{
		Task.Run(async () =>
		{
			await Database.InitializeAsync();
			await Database.PurgeOldDataAsync();
		});
	}

	private void HandleHotReload()
	{
		// On hot-reload, none of the events that normally bootstrap plugin state fire:
		//   * OnMapLoad — WeaponCache stays empty → point handlers can't resolve the
		//     weapon used for a kill/hit, so kills/hits don't credit points.
		//   * EventPlayerActivate — pre-existing players never enter the load pipeline,
		//     so PlayerData isn't fetched and ModifyPoints has nothing to write to.
		//   * OnRoundPrestart — Scoreboard.UpdateAllScoreboards isn't called, but
		//     each completed load re-populates the scoreboard cache via
		//     UpdatePlayerScoreboard as a side effect, so this is covered.
		// Explicitly do the equivalent setup here so a hot-reload becomes functional
		// immediately without waiting for the next map change.
		//
		// The whole bootstrap defers to NextWorldUpdate: Load() runs very early in
		// the plugin lifecycle and SwiftlyS2's internal state (PlayerManager entries,
		// event pipes, IPlayer entity refs) may not be fully re-registered for the
		// new plugin instance until the current world tick completes. Enumerating
		// IPlayer at Load() time can return stale refs where SteamID reads to 0 or
		// the controller is not valid yet. NextWorldUpdate matches the timing
		// of a normal game event handler like OnPlayerActivate, which is why the
		// per-connect load path works reliably.
		Core.Scheduler.NextWorldUpdate(() =>
		{
			// WeaponCache walks the game's item schema via Core.Helpers, so it must
			// run on the main thread. Idempotent — bails if already initialized.
			WeaponCache.Initialize();

			// Broadcast RevealAll for the existing player cohort. On a plain hot-
			// reload no EventPlayerActivate fires for players who were already
			// connected before the reload, so without this broadcast they would
			// lose the reveal grant until they reconnect. Cheap and idempotent.
			Scoreboard.SendRevealAll();

			// Loads issued before the database is ready fail and are retried by the save
			// timer; waiting for initialization just makes the first attempt succeed.
			Task.Run(async () =>
			{
				await Database.InitializeAsync();
				Core.Scheduler.NextWorldUpdate(PlayerData.LoadMissingPlayers);
			});
		});
	}

	/* ==================== Configuration Loading ==================== */

	private void InitializeConfigs()
	{
		Config = BuildConfigService<PluginConfig>("config.json", "K4Ranks");
		Points = BuildConfigService<PointsConfig>("points.json", "K4RanksPoints");
		RanksConfig = BuildConfigService<RanksConfig>("ranks.json", "K4RanksRanks");
		Commands = BuildConfigService<CommandsConfig>("commands.json", "K4RanksCommands");
		Modules = BuildConfigService<ModuleConfig>("modules.json", "K4RanksModules");
	}

	private IOptionsMonitor<T> BuildConfigService<T>(string fileName, string sectionName) where T : class, new()
	{
		Core.Configuration
			.InitializeJsonWithModel<T>(fileName, sectionName)
			.Configure(cfg => cfg.AddJsonFile(Core.Configuration.GetConfigPath(fileName), optional: false, reloadOnChange: true));

		ServiceCollection services = new();
		services.AddSwiftly(Core)
			.AddOptions<T>()
			.BindConfiguration(sectionName);

		var provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IOptionsMonitor<T>>();
	}

	/* ==================== Shared API ==================== */

	public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
	{
		const string apiVersion = "K4LevelRanks.Api.v1";

		var apiService = new K4RanksApiService(this);
		interfaceManager.AddSharedInterface<IK4RanksApi, K4RanksApiService>(apiVersion, apiService);

		Core.Logger.LogInformation("Shared API registered: {Version}", apiVersion);
	}

	/* ==================== Unload ==================== */

	public override void Unload()
	{
		Scoreboard.Stop();
		StopSaveTimer();

		// Bounded: an unreachable database must not hang shutdown or a hot reload.
		PlayerData.FlushAndStop(TimeSpan.FromSeconds(8));

		WeaponCache.Reset();
	}
}
