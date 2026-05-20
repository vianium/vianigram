// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
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
using Vianigram.Settings.Infrastructure;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Application.Handlers
{
    /// <summary>
    /// Persists a typed value under a <see cref="PreferenceKey"/>. Mutates the
    /// in-process aggregate first (so the staged
    /// <see cref="Domain.Events.PreferenceChanged"/> event captures the actual
    /// transition), serializes to the canonical string, and writes through to
    /// the underlying <see cref="IPreferencesStore"/>. Domain events are
    /// drained after the persistence write succeeds.
    ///
    /// V1 catalog has no per-key validators; the application layer keeps the
    /// hook for the architecture-doc <c>IPreferenceValidator&lt;T&gt;</c>
    /// extension and currently rejects only obviously invalid inputs (null
    /// key, null value when <typeparamref name="T"/> is a reference type that
    /// the catalog explicitly disallows).
    /// </summary>
    internal sealed class SetPreferenceHandler<T>
    {
        private readonly UserPreferences _aggregate;
        private readonly IPreferencesStore _store;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SetPreferenceHandler(
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
            _log = new TimestampedLogger(log, "Settings.SetPreference");
            _clock = clock;
        }

        public async Task<Result<Unit, SettingsError>> HandleAsync(SetPreferenceCommand<T> cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("null command"));
            if (cmd.Key == null) return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("key required"));

            DateTime now = _clock.UtcNow;

            string canonical = PreferenceSerializer.Serialize(cmd.Value);
            var write = await _store.SetRawAsync(cmd.Key.Name, canonical, ct).ConfigureAwait(false);
            if (write.IsFail)
            {
                _log.Warn("store.SetRaw('" + cmd.Key.Name + "') failed: " + write.Error);
                return write;
            }

            _aggregate.Set<T>(cmd.Key, cmd.Value, now);
            HandlerEventBridge.Drain(_aggregate, _bus);
            return Result<Unit, SettingsError>.Ok(Unit.Value);
        }
    }
}
