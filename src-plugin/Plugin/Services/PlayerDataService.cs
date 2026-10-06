using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;

namespace K4Ranks;

public sealed partial class Plugin
{
	public sealed partial class PlayerDataService(Plugin plugin)
	{
		/* ==================== Fields ==================== */

		private readonly Plugin _plugin = plugin;
		private readonly ConcurrentDictionary<ulong, PlayerData> _playerData = new();

		/// <summary>SteamIDs with a load in flight, so a repeated player_activate can't start a second one.</summary>
		private readonly ConcurrentDictionary<ulong, byte> _loadInFlight = new();

		/* ==================== Slot Cache ==================== */

		private const int MaxSlots = 64;

		/// <summary>
		/// Identity of the player object currently in a slot, resolved once so per-event
		/// lookups don't re-read the native SteamID / IsFakeClient.
		/// </summary>
		public sealed class SlotEntry(IPlayer player, bool isBot, ulong steamId, ConcurrentDictionary<ulong, PlayerData> records)
		{
			public readonly IPlayer Player = player;
			public readonly bool IsBot = isBot;
			public readonly ulong SteamId = steamId;

			/// <summary>Looked up live, so a record removed on disconnect is never served.</summary>
			public PlayerData? Data => !IsBot && SteamId != 0 && records.TryGetValue(SteamId, out var data) ? data : null;
		}

		private readonly SlotEntry?[] _slots = new SlotEntry?[MaxSlots];

		/// <summary>
		/// Cached identity for <paramref name="player"/>, or null once it is no longer the
		/// slot's current (connected) player object.
		/// </summary>
		internal SlotEntry? Resolve(IPlayer? player)
		{
			if (player is null)
				return null;

			var slot = player.PlayerID;
			if ((uint)slot >= MaxSlots || !ReferenceEquals(Core.PlayerManager.GetPlayer(slot), player))
				return null;

			var entry = Volatile.Read(ref _slots[slot]);
			if (entry is not null && ReferenceEquals(entry.Player, player))
				return entry;

			entry = new SlotEntry(player, player.IsFakeClient, player.SteamID, _playerData);

			// A human reads SteamID 0 until Steam auth lands; don't pin that.
			if (entry.IsBot || entry.SteamId != 0)
				Volatile.Write(ref _slots[slot], entry);

			return entry;
		}

		/// <summary>Humans passing <see cref="IPlayer.IsValid"/>; bots excluded.</summary>
		internal int CountValidHumans()
		{
			var players = Core.PlayerManager;
			var count = 0;

			for (var slot = 0; slot < MaxSlots; slot++)
			{
				var player = players.GetPlayer(slot);
				if (player is not null && Resolve(player) is { IsBot: false } && player.IsValid)
					count++;
			}

			return count;
		}

		/* ==================== Public Accessors ==================== */

		public PlayerData? GetPlayerData(IPlayer player) => Resolve(player)?.Data;

		/// <summary>Records of connected players; every tracked record is fully loaded.</summary>
		public ICollection<PlayerData> GetAllLoadedPlayers() => _playerData.Values;

		/* ==================== Load ==================== */

		/// <summary>
		/// Starts tracking a connected human unless already tracked or loading. Game thread only:
		/// every native read happens here, so the async load never touches the player object.
		/// </summary>
		public void EnsureLoaded(IPlayer player)
		{
			if (Resolve(player) is not { IsBot: false, SteamId: not 0 and var steamId })
				return;

			if (_playerData.ContainsKey(steamId))
				return;

			// Back before the last session was saved: that record is newer than the database.
			if (_departed.TryRemove(steamId, out var departed))
			{
				Track(player, steamId, departed);
				return;
			}

			// Still connecting; the periodic retry comes back once the controller exists.
			if (player.Controller is not { IsValid: true } controller)
				return;

			if (!_loadInFlight.TryAdd(steamId, 0))
				return;

			var name = SanitizeName(controller.PlayerName) ?? "";
			_ = Task.Run(() => LoadAsync(steamId, name));
		}

		/// <summary>Retries anyone connected but untracked: a failed load, or Steam auth that landed late.</summary>
		internal void LoadMissingPlayers()
		{
			var players = Core.PlayerManager;

			for (var slot = 0; slot < MaxSlots; slot++)
			{
				if (players.GetPlayer(slot) is { } player)
					EnsureLoaded(player);
			}
		}

		private async Task LoadAsync(ulong steamId, string name)
		{
			PlayerRecord? record = null;

			try
			{
				record = await _plugin.Database.LoadPlayerAsync(steamId.ToString());
			}
			catch (Exception)
			{
				// Already logged. No placeholder record: its next save would overwrite the real rows.
			}

			Core.Scheduler.NextWorldUpdate(() => CompleteLoad(steamId, name, record));
		}

		private void CompleteLoad(ulong steamId, string name, PlayerRecord? record)
		{
			_loadInFlight.TryRemove(steamId, out _);

			if (record is null || _playerData.ContainsKey(steamId))
				return;

			// By SteamID, not the original player object, so a reconnect during the load still lands.
			var player = FindConnected(steamId);
			if (player is null)
				return;

			if (_departed.TryRemove(steamId, out var departed))
			{
				Track(player, steamId, departed);
				return;
			}

			Track(player, steamId, BuildRecord(steamId, name, record));
		}

		private void Track(IPlayer player, ulong steamId, PlayerData data)
		{
			// Time spent away is not playtime.
			data.SessionStartTime = DateTime.UtcNow;
			_playerData[steamId] = data;

			_plugin.Scoreboard.UpdatePlayerScoreboard(player);

			if (_plugin.Config.CurrentValue.Scoreboard.Clantags)
				UpdatePlayerClanTag(player, data);
		}

		private PlayerData BuildRecord(ulong steamId, string name, PlayerRecord record)
		{
			var steam = steamId.ToString();
			var data = record.Base ?? new PlayerData { Value = _plugin.Config.CurrentValue.Rank.StartPoints };

			data.Steam = steam;
			if (name.Length > 0)
				data.Name = name;

			data.Rank = _plugin.Ranks.GetRankId(data.Points);
			data.Settings = record.Settings ?? new PlayerSettings { Steam = steam };
			data.HitData = record.Hits ?? new HitData { Steam = steam };
			data.WeaponStats.LoadFrom(record.Weapons);
			data.IsLoaded = true;

			// What the database holds is the baseline later saves diff against; a new player's row is written on the next save.
			data.MarkSynced(baseRowMissing: record.Base is null);
			return data;
		}

		private IPlayer? FindConnected(ulong steamId)
		{
			var players = Core.PlayerManager;

			for (var slot = 0; slot < MaxSlots; slot++)
			{
				var player = players.GetPlayer(slot);
				if (player is not null && Resolve(player) is { IsBot: false } entry && entry.SteamId == steamId)
					return player;
			}

			return null;
		}

		/// <summary>
		/// Sanitizes a player name for safe database storage.
		/// Removes null bytes (rejected by MySQL VARCHAR columns regardless of charset)
		/// and truncates to the maximum column length.
		/// Supplementary Unicode characters (code points > U+FFFF) such as Mathematical
		/// Fraktur, emoji, and other non-BMP scripts are preserved — the name column
		/// uses utf8mb4 (see M005_NameColumnUtf8mb4) which supports the full Unicode range.
		/// </summary>
		private static string? SanitizeName(string? name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			var sb = new System.Text.StringBuilder(name.Length);
			foreach (var c in name)
			{
				if (c == '\0') continue;               // null byte – MySQL VARCHAR rejects this
				sb.Append(c);
			}

			const int MaxNameLength = 128;             // must match M004_ExtendNameColumn
			var result = sb.ToString();
			return result.Length > MaxNameLength ? result[..MaxNameLength] : result;
		}

		/* ==================== Points ==================== */

		public void ModifyPoints(IPlayer player, int amount, string reason, bool showMessage = true, string? otherPlayerName = null)
		{
			var entry = Resolve(player);
			var data = entry?.Data;
			if (data is not { IsLoaded: true })
				return;

			if (amount == 0)
				return;

			var cfg = _plugin.Config.CurrentValue;
			amount = ApplyVipMultiplier(entry!.SteamId, amount, cfg.Vip);

			var oldRank = _plugin.Ranks.GetRank(data.Points);
			data.Points += amount;
			data.RoundPoints += amount;
			var newRank = _plugin.Ranks.GetRank(data.Points);

			if (cfg.Scoreboard.Clantags && oldRank.Name != newRank.Name)
				UpdatePlayerClanTag(player, data);

			if (cfg.Scoreboard.ScoreSync)
			{
				var controller = player.Controller;
				controller?.Score = data.Points;
				controller?.ScoreUpdated();
			}

			if (showMessage && !cfg.Points.RoundEndSummary && data.PointMessagesEnabled)
			{
				var displayName = cfg.Points.ShowPlayerNames ? otherPlayerName : null;
				ShowPointMessage(player, amount, reason, displayName);
			}

			if (oldRank.Name != newRank.Name)
			{
				data.Rank = _plugin.Ranks.GetRankId(data.Points);
				ShowRankChangeMessage(player, oldRank, newRank, amount > 0);
			}
		}

		private static int ApplyVipMultiplier(ulong steamId, int amount, VipSettings vip)
		{
			if (amount <= 0)
				return amount;

			if (vip.Multiplier <= 1.0 || vip.Flags.Count == 0)
				return amount;

			if (!IsVipPlayer(steamId, vip.Flags))
				return amount;

			return (int)(amount * vip.Multiplier);
		}

		private static bool IsVipPlayer(ulong steamId, List<string> flags)
		{
			foreach (var flag in flags)
			{
				if (Core.Permission.PlayerHasPermission(steamId, flag))
					return true;
			}

			return false;
		}

		/* ==================== Messages ==================== */

		private static void ShowPointMessage(IPlayer player, int amount, string reasonKey, string? otherPlayerName = null)
		{
			var localizer = Core.Translation.GetPlayerLocalizer(player);
			var prefix = localizer["k4.general.prefix"];
			var reason = localizer[reasonKey];

			if (!string.IsNullOrEmpty(otherPlayerName))
				reason = $"{reason} ({otherPlayerName})";

			var messageKey = amount > 0 ? "k4.chat.points.gained" : "k4.chat.points.lost";
			player.SendChat($"{prefix} {localizer[messageKey, Math.Abs(amount), reason]}");
		}

		private static void ShowRankChangeMessage(IPlayer player, Rank oldRank, Rank newRank, bool promoted)
		{
			var localizer = Core.Translation.GetPlayerLocalizer(player);
			var prefix = localizer["k4.general.prefix"];

			var messageKey = promoted ? "k4.chat.rank.promoted" : "k4.chat.rank.demoted";
			player.SendChat($"{prefix} {localizer[messageKey, newRank.ChatColor, newRank.Name]}");
		}

		public void ShowRoundSummary(IPlayer player)
		{
			var data = GetPlayerData(player);
			if (data is not { IsLoaded: true })
				return;

			if (data.RoundPoints == 0 || !data.PointMessagesEnabled)
				return;

			var localizer = Core.Translation.GetPlayerLocalizer(player);
			var prefix = localizer["k4.general.prefix"];

			var messageKey = data.RoundPoints > 0 ? "k4.chat.summary.gained" : "k4.chat.summary.lost";
			player.SendChat($"{prefix} {localizer[messageKey, Math.Abs(data.RoundPoints)]}");
		}

		/* ==================== Helpers ==================== */

		internal void UpdatePlayerClanTag(IPlayer player, PlayerData data)
		{
			var rank = _plugin.Ranks.GetRank(data.Points);
			var controller = player.Controller;

			if (controller != null)
			{
				controller.Clan = rank.Tag;
				controller.ClanUpdated();
			}
		}
	}
}
