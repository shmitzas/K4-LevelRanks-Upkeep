using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace K4Ranks;

public sealed partial class Plugin
{
	public sealed partial class PlayerDataService
	{
		/* ==================== Save Pipeline ==================== */

		// One background writer saves every changed player, one transaction each, on one connection
		// per pass. A record counts as saved only once its transaction commits, so a failed save is
		// simply written again with the then-latest values. A player who leaves keeps their record
		// here until it is saved, and gets it back on a quick reconnect.

		private const int UnloadWriterWaitMs = 2000;

		private readonly ConcurrentDictionary<ulong, PlayerData> _departed = new();
		private readonly SemaphoreSlim _saveSignal = new(0, 1);
		private readonly SemaphoreSlim _passGate = new(1, 1);
		private readonly CancellationTokenSource _writerCts = new();
		private Task _writer = Task.CompletedTask;

		internal void StartWriter() => _writer = Task.Run(() => RunWriterAsync(_writerCts.Token));

		/// <summary>Game thread: credits playtime so far and wakes the writer. Calls coalesce.</summary>
		public void RequestSave()
		{
			foreach (var data in _playerData.Values)
				data.UpdatePlaytime();

			WakeWriter();
		}

		/// <summary>Game thread: stops tracking a player who left; the record stays until it is saved.</summary>
		public void MarkDeparted(ulong steamId)
		{
			if (!_playerData.TryRemove(steamId, out var data))
				return;

			data.UpdatePlaytime();
			_departed[steamId] = data;
			WakeWriter();
		}

		private void WakeWriter()
		{
			try
			{
				_saveSignal.Release();
			}
			catch (SemaphoreFullException)
			{
				// Already signalled; the pending pass picks this up too.
			}
		}

		private async Task RunWriterAsync(CancellationToken ct)
		{
			var failures = 0;

			while (!ct.IsCancellationRequested)
			{
				try
				{
					// After a failed pass, retry on a backoff rather than waiting for the next request.
					if (failures == 0)
						await _saveSignal.WaitAsync(ct);
					else
						await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures - 1, 5))), ct);

					var (failed, error) = await RunPassAsync(ct);

					if (failed == 0)
					{
						if (failures > 0)
							Core.Logger.LogInformation("Ranks saves recovered after {Attempts} failed attempt(s)", failures);

						failures = 0;
					}
					else
					{
						ReportFailedPass(++failures, failed, error);
					}
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					return;
				}
				catch (Exception ex)
				{
					// The pass could not start at all, e.g. the database is unreachable.
					ReportFailedPass(++failures, -1, ex);
				}
			}
		}

		private void ReportFailedPass(int attempt, int failedPlayers, Exception? error)
		{
			var pending = _playerData.Values.Concat(_departed.Values).Distinct().Count(d => d.HasPendingChanges());

			if (attempt == 1)
			{
				Core.Logger.LogWarning(error,
					"Saving ranks failed{Players}; {Pending} player(s) keep their changes and are retried",
					failedPlayers < 0 ? "" : $" for {failedPlayers} player(s)", pending);
				return;
			}

			// Every retry for the first few, then every tenth, so an outage cannot flood the log.
			if (attempt <= 5 || attempt % 10 == 0)
				Core.Logger.LogError(error,
					"Saving ranks has failed {Attempts} times in a row; {Pending} player(s) pending, {Departed} departed player(s) held in memory",
					attempt, pending, _departed.Count);
		}

		/// <summary>Runs one save pass; never two at once, whoever calls.</summary>
		private async Task<(int Failed, Exception? Error)> RunPassAsync(CancellationToken ct)
		{
			await _passGate.WaitAsync(ct);

			try
			{
				return await SavePassAsync(ct);
			}
			finally
			{
				_passGate.Release();
				ForgetSavedDeparted();
			}
		}

		/// <summary>Writes every changed record; returns how many could not be saved and the first error.</summary>
		private async Task<(int Failed, Exception? Error)> SavePassAsync(CancellationToken ct)
		{
			// Distinct: a reconnect adopting a departed record mid-pass must not save it twice.
			var records = _playerData.Values.Concat(_departed.Values).Distinct().Where(d => d.HasPendingChanges()).ToList();
			if (records.Count == 0)
				return (0, null);

			var database = _plugin.Database;
			await using var connection = await database.OpenSaveConnectionAsync(ct);

			var failed = 0;
			Exception? firstError = null;

			for (var i = 0; i < records.Count; i++)
			{
				var data = records[i];

				// Snapshot per player, right before its transaction, so it carries the latest values.
				if (data.BuildSaveEntry((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()) is not { } entry)
					continue;

				try
				{
					await database.SavePlayerAsync(connection, entry, ct);
					data.AcknowledgeSync();
				}
				catch (OperationCanceledException) when (ct.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					failed++;
					firstError ??= ex;

					// A lost connection fails everyone left; leave them for the retry.
					if (connection.State != System.Data.ConnectionState.Open)
						return (failed + records.Count - i - 1, firstError);
				}
			}

			return (failed, firstError);
		}

		/// <summary>Drops departed records that are fully saved; a reconnect may have taken one back meanwhile.</summary>
		private void ForgetSavedDeparted()
		{
			foreach (var pair in _departed)
			{
				if (!pair.Value.HasPendingChanges())
					_departed.TryRemove(pair);
			}
		}

		/// <summary>
		/// Unload: stops the writer, then saves everything still pending, bounded by <paramref name="budget"/>.
		/// Blocks the calling (game) thread for at most about that long, whatever the database does.
		/// </summary>
		public void FlushAndStop(TimeSpan budget)
		{
			var deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;

			foreach (var data in _playerData.Values)
				data.UpdatePlaytime();

			_writerCts.Cancel();

			try
			{
				_writer.Wait(UnloadWriterWaitMs);
			}
			catch (AggregateException)
			{
				// The writer logs its own failures.
			}

			while (true)
			{
				var remaining = deadline - Environment.TickCount64;
				if (remaining <= 0)
					break;

				using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(remaining));

				// Off the game thread so no captured context can deadlock, and waited with a timeout
				// because a provider can ignore cancellation while connecting.
				var pass = Task.Run(() => RunPassAsync(cts.Token));

				try
				{
					if (pass.Wait(TimeSpan.FromMilliseconds(remaining)) && pass.Result.Failed == 0)
						return;
				}
				catch (AggregateException)
				{
					// Unreachable database or cancelled at the deadline; retried below while time remains.
				}

				Thread.Sleep((int)Math.Clamp(deadline - Environment.TickCount64, 0, 500));
			}

			var unsaved = _playerData.Values.Concat(_departed.Values).Distinct().Count(d => d.HasPendingChanges());
			if (unsaved > 0)
				Core.Logger.LogError("Ranks: unsaved changes for {Players} player(s) were lost at unload (database unreachable)", unsaved);
		}
	}
}
