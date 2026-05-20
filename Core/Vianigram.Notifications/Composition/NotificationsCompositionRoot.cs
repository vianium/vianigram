// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Notifications.Application;
using Vianigram.Notifications.Infrastructure;
using Vianigram.Notifications.Ports.Inbound;
using Vianigram.Notifications.Ports.Outbound;

namespace Vianigram.Notifications.Composition
{
    /// <summary>
    /// Composition root for the Notifications bounded context.
    ///
    /// Wires the in-memory profile repository and the supplied
    /// platform notifier + ACL-shared MTProto RPC adapter into a
    /// <see cref="NotificationsApplication"/> instance, which is the single
    /// public entry point (<see cref="INotificationsApi"/>).
    ///
    /// Mirrors the <c>Vianigram.Stickers.Composition.StickersCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls <see cref="Build"/>
    /// and stores the returned <see cref="INotificationsApi"/> in its own
    /// service registry (e.g. <c>root.Register&lt;INotificationsApi&gt;(api)</c>).
    ///
    /// The kernel rule that contexts don't reference each other's ports is
    /// upheld: the same <c>IMtProtoRpcPort</c> reference passed in here
    /// implements every per-context interface — it's the same adapter object
    /// surfaced as different types.
    /// </summary>
    public static class NotificationsCompositionRoot
    {
        /// <summary>
        /// Builds the Notifications application surface and returns the
        /// inbound API. The host composition root is responsible for storing
        /// the returned instance in whatever service container it uses.
        /// </summary>
        public static INotificationsApi Build(
            IMtProtoRpcPort rpc,
            IPlatformNotifier notifier,
            INotificationProfileRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            return Build(rpc, notifier, repo, bus, logger, clock, null);
        }

        public static INotificationsApi Build(
            IMtProtoRpcPort rpc,
            IPlatformNotifier notifier,
            INotificationProfileRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IPeerAccessHashPort peerHashes)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (notifier == null) throw new ArgumentNullException("notifier");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new NotificationsApplication(rpc, notifier, repo, bus, logger, clock, peerHashes);
        }

        /// <summary>
        /// Convenience overload that builds the in-memory repository and
        /// stub platform notifier for callers that only need the V1 wiring
        /// (no Storage adapter / no real WinRT sinks yet).
        /// </summary>
        public static INotificationsApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            return Build(rpc, bus, logger, clock, null);
        }

        public static INotificationsApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IPeerAccessHashPort peerHashes)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new NotificationsApplication(
                rpc,
                new StubPlatformNotifier(logger),
                new InMemoryNotificationProfileRepository(),
                bus,
                logger,
                clock,
                peerHashes);
        }
    }
}
