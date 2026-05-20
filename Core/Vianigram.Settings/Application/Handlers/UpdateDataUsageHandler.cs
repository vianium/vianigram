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
    /// Persists a <see cref="DataUsagePolicy"/> under the matching
    /// per-network composite key (<c>network.auto_download.wifi</c> /
    /// <c>.cellular</c> / <c>.roaming</c>), mirrors it on the aggregate, and
    /// stages a <c>DataPolicyChanged</c> domain event so the media downloader
    /// can reroute auto-download decisions.
    /// </summary>
    internal sealed class UpdateDataUsageHandler
    {
        private readonly UserPreferences _aggregate;
        private readonly IPreferencesStore _store;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public UpdateDataUsageHandler(
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
            _log = new TimestampedLogger(log, "Settings.UpdateDataUsage");
            _clock = clock;
        }

        public async Task<Result<Unit, SettingsError>> HandleAsync(UpdateDataUsageCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("null command"));
            if (cmd.Policy == null)
                return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("policy required"));

            PreferenceKey key = PreferenceKeys.DataUsageFor(cmd.Policy.Network);
            DateTime now = _clock.UtcNow;

            DataUsagePolicy fallback = ResolveDefault(cmd.Policy.Network);
            DataUsagePolicy oldPolicy = _aggregate.GetOrDefault<DataUsagePolicy>(key, fallback);

            string canonical = PreferenceSerializer.Serialize(cmd.Policy);
            var write = await _store.SetRawAsync(key.Name, canonical, ct).ConfigureAwait(false);
            if (write.IsFail)
            {
                _log.Warn("update data usage (" + cmd.Policy.Network + "): store write failed: " + write.Error);
                return write;
            }

            _aggregate.Set<DataUsagePolicy>(key, cmd.Policy, now);
            _aggregate.RecordDataPolicyChanged(cmd.Policy.Network, oldPolicy, cmd.Policy, now);
            HandlerEventBridge.Drain(_aggregate, _bus);
            return Result<Unit, SettingsError>.Ok(Unit.Value);
        }

        private static DataUsagePolicy ResolveDefault(NetworkKind network)
        {
            switch (network)
            {
                case NetworkKind.WiFi: return DataUsagePolicy.DefaultWiFi;
                case NetworkKind.Cellular: return DataUsagePolicy.DefaultCellular;
                case NetworkKind.Roaming: return DataUsagePolicy.DefaultRoaming;
                default: return DataUsagePolicy.DefaultWiFi;
            }
        }
    }
}
