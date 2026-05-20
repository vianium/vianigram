// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Settings.Application.UseCases;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.Entities;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Application.Handlers
{
    /// <summary>
    /// Wipes every stored preference and clears the aggregate. Emits a single
    /// <c>PreferencesReset</c> domain event with the count of keys that were
    /// removed; subscribers re-query for fresh values rather than receiving
    /// per-key change notifications.
    /// </summary>
    internal sealed class ResetToDefaultsHandler
    {
        private readonly UserPreferences _aggregate;
        private readonly IPreferencesStore _store;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ResetToDefaultsHandler(
            UserPreferences aggregate,
            IPreferencesStore store,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (store == null) throw new ArgumentNullException("store");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _aggregate = aggregate;
            _store = store;
            _bus = bus;
            _log = new TimestampedLogger(log, "Settings.ResetToDefaults");
            _clock = clock;
        }

        public async Task<Result<Unit, SettingsError>> HandleAsync(ResetToDefaultsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("null command"));

            var snapshot = await _store.GetAllAsync(ct).ConfigureAwait(false);
            if (snapshot.IsFail)
            {
                _log.Warn("reset: GetAll failed: " + snapshot.Error);
                return Result<Unit, SettingsError>.Fail(snapshot.Error);
            }

            var keys = new List<string>(snapshot.Value.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var rm = await _store.RemoveAsync(keys[i], ct).ConfigureAwait(false);
                if (rm.IsFail)
                {
                    _log.Warn("reset: Remove('" + keys[i] + "') failed: " + rm.Error);
                    // Keep going — partial reset is preferable to abort.
                }
            }

            DateTime now = _clock.UtcNow;
            _aggregate.Reset(now);
            HandlerEventBridge.Drain(_aggregate, _bus);
            return Result<Unit, SettingsError>.Ok(Unit.Value);
        }
    }
}
