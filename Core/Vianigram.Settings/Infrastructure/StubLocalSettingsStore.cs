// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Placeholder for an <see cref="IPreferencesStore"/> backed by
    /// <c>Windows.Storage.ApplicationData.Current.LocalSettings.Values</c>.
    /// Documents the shape the real adapter will expose without dragging the
    /// WinRT dependency into this PCL-shaped bounded context.
    ///
    /// Behavior: every call logs the intent and delegates to an in-process
    /// fallback (<see cref="InMemoryPreferencesStore"/>) so consumers can run
    /// end-to-end smoke tests against a host that hasn't wired the real adapter
    /// yet. The real implementation lives in <c>Vianigram.Storage</c> (or the
    /// App layer) and:
    ///
    ///   * Reads/writes values via <c>LocalSettings.Values[key]</c>.
    ///   * Clamps payload size to 8 KB per value (WP8.1 quota); larger values
    ///     fall through to <c>LocalFolder/preferences.json</c> via
    ///     <c>FileSettingsStorage</c>.
    ///   * Surfaces <c>UnauthorizedAccessException</c> / disk-full faults as
    ///     <see cref="SettingsError.StorageError"/> with the underlying
    ///     <see cref="Exception"/> attached.
    /// </summary>
    public sealed class StubLocalSettingsStore : IPreferencesStore
    {
        private readonly IComponentLogger _log;
        private readonly InMemoryPreferencesStore _fallback;

        public StubLocalSettingsStore(ILogger log)
        {
            if (log == null) throw new ArgumentNullException("log");
            _log = new TimestampedLogger(log, "Settings.StubLocalSettingsStore");
            _fallback = new InMemoryPreferencesStore();
        }

        public async Task<Result<string, SettingsError>> GetRawAsync(string key, CancellationToken ct)
        {
            _log.Info("LocalSettings stub: GetRawAsync('" + (key ?? string.Empty) + "')");
            return await _fallback.GetRawAsync(key, ct).ConfigureAwait(false);
        }

        public async Task<Result<Unit, SettingsError>> SetRawAsync(string key, string value, CancellationToken ct)
        {
            _log.Info("LocalSettings stub: SetRawAsync('" + (key ?? string.Empty) + "')");
            return await _fallback.SetRawAsync(key, value, ct).ConfigureAwait(false);
        }

        public async Task<Result<Unit, SettingsError>> RemoveAsync(string key, CancellationToken ct)
        {
            _log.Info("LocalSettings stub: RemoveAsync('" + (key ?? string.Empty) + "')");
            return await _fallback.RemoveAsync(key, ct).ConfigureAwait(false);
        }

        public async Task<Result<IDictionary<string, string>, SettingsError>> GetAllAsync(CancellationToken ct)
        {
            _log.Info("LocalSettings stub: GetAllAsync");
            return await _fallback.GetAllAsync(ct).ConfigureAwait(false);
        }
    }
}
