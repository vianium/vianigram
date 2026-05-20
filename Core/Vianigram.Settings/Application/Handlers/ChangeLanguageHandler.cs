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
    /// Switches the active language pack. Persists the new
    /// <see cref="LanguagePack"/> under <c>language.pack</c> (version stamp
    /// reset to 0 so the next sync triggers a full <c>langpack.getLangPack</c>),
    /// mirrors it on the aggregate, and stages a <c>LanguageChanged</c>
    /// domain event so the I18n / App layer can reload the strings table.
    /// </summary>
    internal sealed class ChangeLanguageHandler
    {
        private readonly UserPreferences _aggregate;
        private readonly IPreferencesStore _store;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ChangeLanguageHandler(
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
            _log = new TimestampedLogger(log, "Settings.ChangeLanguage");
            _clock = clock;
        }

        public async Task<Result<Unit, SettingsError>> HandleAsync(ChangeLanguageCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("null command"));
            if (string.IsNullOrEmpty(cmd.LangCode))
                return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("langCode required"));

            DateTime now = _clock.UtcNow;
            LanguagePack oldPack = _aggregate.GetOrDefault<LanguagePack>(PreferenceKeys.LanguagePack, LanguagePack.Default);
            // Reset version to 0 so the next SyncFromServer pulls a full pack.
            LanguagePack newPack = new LanguagePack(cmd.LangCode, 0, oldPack != null ? oldPack.BaseLangCode : null);

            string canonical = PreferenceSerializer.Serialize(newPack);
            var write = await _store.SetRawAsync(PreferenceKeys.LanguagePack.Name, canonical, ct).ConfigureAwait(false);
            if (write.IsFail)
            {
                _log.Warn("change language: store write failed: " + write.Error);
                return write;
            }

            _aggregate.Set<LanguagePack>(PreferenceKeys.LanguagePack, newPack, now);
            _aggregate.RecordLanguageChanged(oldPack, newPack, now);
            HandlerEventBridge.Drain(_aggregate, _bus);
            return Result<Unit, SettingsError>.Ok(Unit.Value);
        }
    }
}
