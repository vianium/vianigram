// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Settings.Application;
using Vianigram.Settings.Infrastructure;
using Vianigram.Settings.Ports.Inbound;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Composition
{
    /// <summary>
    /// Composition root for the Settings bounded context.
    ///
    /// Wires the in-memory preferences store and the supplied
    /// MTProto RPC adapter into a <see cref="SettingsApplication"/> instance,
    /// which is the single public entry point (<see cref="ISettingsApi"/>).
    ///
    /// Mirrors the
    /// <c>Vianigram.Notifications.Composition.NotificationsCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls <see cref="Build"/>
    /// and stores the returned <see cref="ISettingsApi"/> in its own service
    /// registry (e.g. <c>root.Register&lt;ISettingsApi&gt;(api)</c>).
    ///
    /// The kernel rule that contexts don't reference each other's ports is
    /// upheld: the same <c>IMtProtoRpcPort</c> reference passed in here
    /// implements every per-context interface — it's the same adapter object
    /// surfaced as different types.
    /// </summary>
    public static class SettingsCompositionRoot
    {
        /// <summary>
        /// Builds the Settings application surface and returns the inbound
        /// API. The host composition root is responsible for storing the
        /// returned instance in whatever service container it uses.
        /// </summary>
        public static ISettingsApi Build(
            IMtProtoRpcPort rpc,
            IPreferencesStore store,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            return Build(rpc, store, bus, logger, clock, /* proxySink */ null);
        }

        /// <summary>
        /// Overload that lets the host inject a real
        /// <see cref="IProxyRuntimeSink"/> implementation. When
        /// <paramref name="proxySink"/> is <c>null</c> a NoOp sink is
        /// substituted — saved proxy descriptors persist but never
        /// reach the live transport. Hosts that don't load
        /// Vianigram.MTProto (test runners, design-time previews)
        /// rely on this NoOp behaviour.
        /// </summary>
        public static ISettingsApi Build(
            IMtProtoRpcPort rpc,
            IPreferencesStore store,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IProxyRuntimeSink proxySink)
        {
            return Build(rpc, store, bus, logger, clock, proxySink, /* proxyProbe */ null);
        }

        /// <summary>
        /// Full overload with both the proxy runtime sink and the
        /// live-handshake probe. The probe powers <c>ISettingsApi.TestProxyAsync</c>
        /// — production hosts wire <c>MtProxyProbe</c>; degraded
        /// in-memory hosts pass <c>null</c> to get a NoOp result.
        /// </summary>
        public static ISettingsApi Build(
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

            return new SettingsApplication(rpc, store, bus, logger, clock, proxySink, proxyProbe);
        }

        /// <summary>
        /// Convenience overload that builds the in-memory preferences store
        /// for callers that only need the V1 wiring (no Storage adapter / no
        /// real LocalSettings sink yet).
        /// </summary>
        public static ISettingsApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new SettingsApplication(rpc, new InMemoryPreferencesStore(), bus, logger, clock);
        }
    }
}
