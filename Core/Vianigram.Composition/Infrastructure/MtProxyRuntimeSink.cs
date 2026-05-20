// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProxyRuntimeSink.cs
//
// Bridges the Settings bounded context's outbound IProxyRuntimeSink port
// to the native Vianigram.MTProto WinRT projection. Lives in the
// composition layer because it crosses two bounded contexts (Settings +
// MTProto); neither core context should reach across to the other.
//
// On Apply(ProxyConfig):
//   * Enabled=true  → Vianigram.MTProto.MtProxyRuntime.SetActiveProxy(...)
//   * Enabled=false → Vianigram.MTProto.MtProxyRuntime.ClearActiveProxy()
//
// Failures are logged but never thrown — SettingsApplication catches
// and surfaces a SettingsError.Unknown if the sink throws, but the
// persistence path has already committed so the saved descriptor is
// the source of truth on the next process start.

using System;
using Vianigram.Kernel.Logging;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class MtProxyRuntimeSink : IProxyRuntimeSink
    {
        private readonly IComponentLogger _log;
        // Optional reconnect hook — set by the composition root after
        // the MtProtoChannelAdapter has been built. When non-null we
        // invoke it (fire-and-forget) every time the runtime is
        // re-armed so a saved proxy change takes effect on the next
        // RPC call rather than waiting for the next organic reconnect.
        private Action<ProxyConfig> _reconnectHook;

        public MtProxyRuntimeSink(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            _log = new TimestampedLogger(logger, "MtProxy.Runtime");
        }

        /// <summary>
        /// Register a callback that is invoked after every successful
        /// Apply with the new <see cref="ProxyConfig"/>. The hook runs
        /// on the caller's thread; it should NOT block — production
        /// code spawns its reconnect work via Task.Run so the SetProxy
        /// flow returns immediately to the UI.
        ///
        /// Passing <c>null</c> clears any previously-registered hook.
        /// </summary>
        public void SetReconnectHook(Action<ProxyConfig> hook)
        {
            _reconnectHook = hook;
        }

        public void Apply(ProxyConfig config)
        {
            bool armed;
            try
            {
                if (config == null || !config.Enabled)
                {
                    Vianigram.MTProto.MtProxyRuntime.ClearActiveProxy();
                    _log.Info("MTProxy runtime cleared (direct dial)");
                    armed = true;   // cleared state is still a "successful apply"
                }
                else
                {
                    armed = Vianigram.MTProto.MtProxyRuntime.SetActiveProxy(
                        config.Host,
                        config.Port,
                        config.Secret,
                        (int)config.Mode,
                        config.FakeTlsDomain);
                    if (armed)
                    {
                        _log.Info("MTProxy runtime armed: " + config.Host + ":" + config.Port + " mode=" + config.Mode);
                    }
                    else
                    {
                        _log.Warn("MTProxy runtime rejected SetActiveProxy — descriptor saved but runtime not armed");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("MTProxy runtime apply failed: " + ex.GetType().Name + ": " + ex.Message);
                armed = false;
                // Swallow — the settings layer treats sink exceptions as
                // a runtime-update failure but does not roll back the
                // persisted descriptor. Next process start re-applies
                // from disk via the bootstrap path.
            }

            // Fire the reconnect hook so the active MtProtoChannel
            // reopens with the new transport. Only when the runtime
            // actually changed state — a rejected SetActiveProxy
            // means the wire state is the same and a reopen would
            // just churn an already-good channel.
            if (armed)
            {
                Action<ProxyConfig> hook = _reconnectHook;
                if (hook != null)
                {
                    try { hook(config); }
                    catch (Exception ex)
                    {
                        _log.Warn("ProxyRuntimeSink reconnect hook threw: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
        }
    }
}
