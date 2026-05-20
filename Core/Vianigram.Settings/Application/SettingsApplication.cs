// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Settings.Application.Handlers;
using Vianigram.Settings.Application.UseCases;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.Entities;
using Vianigram.Settings.Domain.Events;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Infrastructure;
using Vianigram.Settings.Ports.Inbound;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Application
{
    /// <summary>
    /// <see cref="ISettingsApi"/> implementation. Dispatches each public method
    /// to the matching handler, surfaces results as
    /// <c>Result&lt;T, SettingsError&gt;</c>, and re-broadcasts internal domain
    /// events on the kernel bus into a CLR event
    /// (<see cref="PreferenceChanged"/>) so XAML / UI consumers don't need an
    /// <see cref="IEventBus"/> dependency.
    ///
    /// All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="SettingsError"/>.
    /// </summary>
    public sealed class SettingsApplication : ISettingsApi, IDisposable
    {
        private readonly UserPreferences _aggregate;
        private readonly IPreferencesStore _store;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        // Raw ILogger retained because nested per-call handler instances
        // (GetPreferenceHandler<T>, SetPreferenceHandler<T>) still consume the
        // ILogger contract; they tag their own component name internally.
        private readonly ILogger _rawLog;
        private readonly IClock _clock;
        // Outbound sink that propagates a saved ProxyConfig into the live
        // MTProto transport. Never null after the ctor — defaults to a
        // NoOp when the host doesn't wire a real implementation (tests,
        // in-memory composition root, smoke runners that don't load
        // Vianigram.MTProto).
        private readonly IProxyRuntimeSink _proxySink;
        // Outbound probe for the "Test connection" button. Live impl
        // does a full MTProxy handshake against a fresh socket; NoOp
        // stub returns Reachable synthetically.
        private readonly IProxyProbe _proxyProbe;

        private readonly ResetToDefaultsHandler _reset;
        private readonly ApplyThemeHandler _applyTheme;
        private readonly ChangeLanguageHandler _changeLang;
        private readonly UpdateDataUsageHandler _updateData;
        private readonly SyncFromServerHandler _sync;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<PreferenceChangedEventArgs> PreferenceChanged;

        public SettingsApplication(
            IMtProtoRpcPort rpc,
            IPreferencesStore store,
            IEventBus bus,
            ILogger logger,
            IClock clock)
            : this(rpc, store, bus, logger, clock, /* proxySink */ null, /* proxyProbe */ null)
        {
        }

        public SettingsApplication(
            IMtProtoRpcPort rpc,
            IPreferencesStore store,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IProxyRuntimeSink proxySink)
            : this(rpc, store, bus, logger, clock, proxySink, /* proxyProbe */ null)
        {
        }

        public SettingsApplication(
            IMtProtoRpcPort rpc,
            IPreferencesStore store,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IProxyRuntimeSink proxySink,
            IProxyProbe proxyProbe)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (store == null) throw new ArgumentNullException("store");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _aggregate = new UserPreferences();
            _store = store;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(logger, "Settings.Application");
            _rawLog = logger;
            _clock = clock;
            _proxySink = proxySink ?? new NoOpProxyRuntimeSink();
            _proxyProbe = proxyProbe ?? new NoOpProxyProbe();

            _reset = new ResetToDefaultsHandler(_aggregate, store, bus, logger, clock);
            _applyTheme = new ApplyThemeHandler(_aggregate, store, bus, logger, clock);
            _changeLang = new ChangeLanguageHandler(_aggregate, store, bus, logger, clock);
            _updateData = new UpdateDataUsageHandler(_aggregate, store, bus, logger, clock);
            _sync = new SyncFromServerHandler(_aggregate, store, rpc, bus, logger, clock);

            _subs = new IDisposable[]
            {
                bus.Subscribe<Domain.Events.PreferenceChanged>(OnPreferenceChanged),
                bus.Subscribe<ThemeChanged>(OnThemeChanged),
                bus.Subscribe<LanguageChanged>(OnLanguageChanged),
                bus.Subscribe<DataPolicyChanged>(OnDataPolicyChanged)
            };
        }

        // ---- ISettingsApi: typed get/set --------------------------------------

        public async Task<Result<T, SettingsError>> GetAsync<T>(PreferenceKey key, CancellationToken ct)
        {
            try
            {
                if (key == null)
                    return Result<T, SettingsError>.Fail(SettingsError.InvalidValue("key required"));

                T fallback = PreferenceDefaults.ResolveDefault<T>(key);
                var handler = new GetPreferenceHandler<T>(_store, _rawLog);
                return await handler.HandleAsync(new GetPreferenceCommand<T>(key, fallback), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<T, SettingsError>.Fail(SettingsError.Unknown("GetAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SettingsError>> SetAsync<T>(PreferenceKey key, T value, CancellationToken ct)
        {
            try
            {
                if (key == null)
                    return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("key required"));

                var handler = new SetPreferenceHandler<T>(_aggregate, _store, _bus, _rawLog, _clock);
                return await handler.HandleAsync(new SetPreferenceCommand<T>(key, value), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("SetAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SettingsError>> ResetAsync(CancellationToken ct)
        {
            try
            {
                return await _reset.HandleAsync(ResetToDefaultsCommand.Default, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("ResetAsync failed", ex));
            }
        }

        // ---- ISettingsApi: theme ----------------------------------------------

        public Task<Result<Theme, SettingsError>> GetThemeAsync(CancellationToken ct)
        {
            return GetAsync<Theme>(PreferenceKeys.Theme, ct);
        }

        public async Task<Result<Unit, SettingsError>> SetThemeAsync(Theme theme, CancellationToken ct)
        {
            try
            {
                return await _applyTheme.HandleAsync(new ApplyThemeCommand(theme), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("SetThemeAsync failed", ex));
            }
        }

        // ---- ISettingsApi: language -------------------------------------------

        public Task<Result<LanguagePack, SettingsError>> GetLanguageAsync(CancellationToken ct)
        {
            return GetAsync<LanguagePack>(PreferenceKeys.LanguagePack, ct);
        }

        public async Task<Result<Unit, SettingsError>> SetLanguageAsync(string langCode, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(langCode))
                    return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("langCode required"));
                return await _changeLang.HandleAsync(new ChangeLanguageCommand(langCode), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("SetLanguageAsync failed", ex));
            }
        }

        // ---- ISettingsApi: data usage ----------------------------------------

        public Task<Result<DataUsagePolicy, SettingsError>> GetDataUsageAsync(NetworkKind network, CancellationToken ct)
        {
            return GetAsync<DataUsagePolicy>(PreferenceKeys.DataUsageFor(network), ct);
        }

        public async Task<Result<Unit, SettingsError>> SetDataUsageAsync(DataUsagePolicy policy, CancellationToken ct)
        {
            try
            {
                if (policy == null)
                    return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("policy required"));
                return await _updateData.HandleAsync(new UpdateDataUsageCommand(policy), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("SetDataUsageAsync failed", ex));
            }
        }

        // ---- ISettingsApi: proxy ----------------------------------------------

        public Task<Result<ProxyConfig, SettingsError>> GetProxyAsync(CancellationToken ct)
        {
            return GetAsync<ProxyConfig>(PreferenceKeys.ProxyMtProto, ct);
        }

        public async Task<ProxyProbeResult> TestProxyAsync(ProxyConfig config, CancellationToken ct)
        {
            if (config == null)
            {
                return ProxyProbeResult.Misconfigured("proxy config required");
            }
            try
            {
                return await _proxyProbe.ProbeAsync(config, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return ProxyProbeResult.NetworkError(0, ex.GetType().Name + ": " + ex.Message);
            }
        }

        public async Task<Result<Unit, SettingsError>> SetProxyAsync(ProxyConfig config, CancellationToken ct)
        {
            try
            {
                if (config == null)
                    return Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("proxy config required"));

                // The ProxyConfig ctor validates host/port/secret-length/SNI
                // when Enabled=true. The catalog-set path serialises via
                // PreferenceSerializer.SerializeProxyConfig, so a saved
                // descriptor always survives a future deserialisation.
                var saved = await SetAsync<ProxyConfig>(PreferenceKeys.ProxyMtProto, config, ct).ConfigureAwait(false);
                if (saved.IsFail) return saved;

                // Persistence succeeded — push to the runtime so the next
                // socket dial picks up the new state. The sink is
                // NoOp-friendly so missing wiring degrades gracefully.
                try
                {
                    _proxySink.Apply(config);
                }
                catch (Exception sinkEx)
                {
                    _log.Warn("ProxyRuntimeSink threw — proxy persisted but runtime not updated: " + sinkEx.Message);
                }

                return saved;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("SetProxyAsync failed", ex));
            }
        }

        // ---- ISettingsApi: server sync ----------------------------------------

        public async Task<Result<Unit, SettingsError>> SyncFromServerAsync(CancellationToken ct)
        {
            try
            {
                return await _sync.HandleAsync(SyncFromServerCommand.Default, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SettingsError>.Fail(SettingsError.Unknown("SyncFromServerAsync failed", ex));
            }
        }

        // ---- bus -> CLR-event bridge -----------------------------------------

        private void OnPreferenceChanged(Domain.Events.PreferenceChanged e)
        {
            RaisePreferenceChanged(new PreferenceChangedEventArgs(e.Key, e.OldValue, e.NewValue, e.At));
        }

        private void OnThemeChanged(ThemeChanged e)
        {
            // Specialized event already mirrored by the generic
            // PreferenceChanged emitted alongside it; subscribers narrow on
            // PreferenceKeys.Theme. We surface a second event with boxed
            // values for symmetry.
            RaisePreferenceChanged(new PreferenceChangedEventArgs(PreferenceKeys.Theme, e.OldTheme, e.NewTheme, e.At));
        }

        private void OnLanguageChanged(LanguageChanged e)
        {
            RaisePreferenceChanged(new PreferenceChangedEventArgs(PreferenceKeys.LanguagePack, e.OldPack, e.NewPack, e.At));
        }

        private void OnDataPolicyChanged(DataPolicyChanged e)
        {
            RaisePreferenceChanged(new PreferenceChangedEventArgs(PreferenceKeys.DataUsageFor(e.Network), e.OldPolicy, e.NewPolicy, e.At));
        }

        private void RaisePreferenceChanged(PreferenceChangedEventArgs args)
        {
            var h = PreferenceChanged;
            if (h == null) return;
            try { h(this, args); }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }
    }
}
