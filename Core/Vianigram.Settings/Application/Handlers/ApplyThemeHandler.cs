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
    /// Switches the active <see cref="Theme"/>. Persists the new value under
    /// <c>appearance.theme_mode</c>, mirrors it on the aggregate, and stages a
    /// <c>ThemeChanged</c> domain event so the App layer can re-apply
    /// resources without polling.
    /// </summary>
    internal sealed class ApplyThemeHandler
    {
        private readonly UserPreferences _aggregate;
        private readonly IPreferencesStore _store;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ApplyThemeHandler(
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
            _log = new TimestampedLogger(log, "Settings.ApplyTheme");
            _clock = clock;
        }

        public async Task<Result<Unit, SettingsError>> HandleAsync(ApplyThemeCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("null command"));

            DateTime now = _clock.UtcNow;
            Theme oldTheme = _aggregate.GetOrDefault<Theme>(PreferenceKeys.Theme, Theme.System);

            string canonical = PreferenceSerializer.Serialize(cmd.Theme);
            var write = await _store.SetRawAsync(PreferenceKeys.Theme.Name, canonical, ct).ConfigureAwait(false);
            if (write.IsFail)
            {
                _log.Warn("apply theme: store write failed: " + write.Error);
                return write;
            }

            _aggregate.Set<Theme>(PreferenceKeys.Theme, cmd.Theme, now);
            _aggregate.RecordThemeChanged(oldTheme, cmd.Theme, now);
            HandlerEventBridge.Drain(_aggregate, _bus);
            return Result<Unit, SettingsError>.Ok(Unit.Value);
        }
    }
}
