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
    /// Re-hydrates Settings from the server.
    ///
    /// Behavior:
    ///   1. Issue <c>langpack.getLangPack#f2f2330a</c> for the active pack
    ///      (or <c>langpack.getDifference#cd984aa5</c> when we already have a
    ///      version stamp). The decoded version updates the stored
    ///      <see cref="LanguagePack"/>.
    ///   2. Issue <c>account.getContentSettings#8b9b4dae</c> to mirror the
    ///      sensitive-content toggle into <c>privacy.sensitive_enabled</c>.
    ///      Failures here are logged at warn level — they do not poison the
    ///      result.
    ///
    /// V1 stops at the version stamp / sensitive flag refresh; the strings
    /// table itself is owned by the future <c>Vianigram.I18n</c> context.
    /// </summary>
    internal sealed class SyncFromServerHandler
    {
        // The langpack name used by Telegram apps for the in-product strings —
        // server expects this verbatim. Mirrors the constant referenced in
        // td/telegram/LanguagePackManager.cpp (LanguageManager::CONTROL_PACK_NAME / CLOUD_PACK_NAMES).
        private const string DefaultLangPack = "android";

        private readonly UserPreferences _aggregate;
        private readonly IPreferencesStore _store;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SyncFromServerHandler(
            UserPreferences aggregate,
            IPreferencesStore store,
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (store == null) throw new ArgumentNullException("store");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _aggregate = aggregate;
            _store = store;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Settings.SyncFromServer");
            _clock = clock;
        }

        public async Task<Result<Unit, SettingsError>> HandleAsync(SyncFromServerCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("null command"));

            DateTime now = _clock.UtcNow;
            LanguagePack current = _aggregate.GetOrDefault<LanguagePack>(PreferenceKeys.LanguagePack, LanguagePack.Default);
            byte[] request;
            if (current.Version > 0)
            {
                request = TlEncoder.EncodeGetDifference(DefaultLangPack, current.LangCode, current.Version);
            }
            else
            {
                request = TlEncoder.EncodeGetLangPack(DefaultLangPack, current.LangCode);
            }

            var langResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (langResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(langResult.Error);
                _log.Warn("langpack sync failed: " + mapped);
                return Result<Unit, SettingsError>.Fail(mapped);
            }

            try
            {
                var diff = TlDecoder.DecodeLangPackDifference(langResult.Value);
                LanguagePack updated = TlDecoder.ToLanguagePack(diff, current.BaseLangCode);
                if (!updated.Equals(current))
                {
                    string canonical = PreferenceSerializer.Serialize(updated);
                    var write = await _store.SetRawAsync(PreferenceKeys.LanguagePack.Name, canonical, ct).ConfigureAwait(false);
                    if (write.IsFail)
                    {
                        _log.Warn("langpack persist failed: " + write.Error);
                        return Result<Unit, SettingsError>.Fail(write.Error);
                    }
                    _aggregate.Set<LanguagePack>(PreferenceKeys.LanguagePack, updated, now);
                    _aggregate.RecordLanguageChanged(current, updated, now);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("langpack decode failed: " + ex.Message);
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("langpack decode failed", ex));
            }

            // Best-effort content settings; failures are non-fatal.
            byte[] csRequest = TlEncoder.EncodeGetContentSettings();
            var csResult = await _rpc.CallAsync(csRequest, ct).ConfigureAwait(false);
            if (csResult.IsFail)
            {
                _log.Info("account.getContentSettings skipped: " + RpcErrorMapper.Map(csResult.Error));
            }
            else
            {
                try
                {
                    var cs = TlDecoder.DecodeContentSettings(csResult.Value);
                    // V1 mirrors the toggle as a plain bool under a dedicated
                    // privacy key; the read-only Privacy lookup will surface it.
                    string privacyKey = "privacy.sensitive_content_enabled";
                    string canonical = PreferenceSerializer.Serialize(cs.SensitiveEnabled);
                    var write = await _store.SetRawAsync(privacyKey, canonical, ct).ConfigureAwait(false);
                    if (write.IsFail)
                    {
                        _log.Warn("sensitive flag persist failed: " + write.Error);
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn("contentSettings decode failed: " + ex.Message);
                }
            }

            HandlerEventBridge.Drain(_aggregate, _bus);
            return Result<Unit, SettingsError>.Ok(Unit.Value);
        }
    }
}
