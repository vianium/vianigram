// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Sync.Application;
using Vianigram.Sync.Infrastructure;
using Vianigram.Sync.Ports.Inbound;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Sync.Composition
{
    /// <summary>
    /// Composition root for the Sync bounded context.
    ///
    /// Wires the four outbound ports (<see cref="IMtProtoRpcPort"/>,
    /// <see cref="IUpdatesPort"/>, <see cref="ISyncStateRepository"/>) plus the
    /// kernel infrastructure (<see cref="IEventBus"/>, <see cref="ILogger"/>,
    /// <see cref="IClock"/>) into a single <see cref="SyncApplication"/>
    /// instance, exposed as <see cref="ISyncApi"/>.
    ///
    /// Mirrors the <c>Vianigram.Account.Composition.AccountCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls <see cref="Register"/>
    /// and stores the returned interface in its own service registry. The kernel
    /// rule that bounded contexts do not reference each other's ports is upheld:
    /// the same physical RPC adapter implements every per-context outbound type.
    /// </summary>
    public static class SyncCompositionRoot
    {
        /// <summary>
        /// Registers the Sync surface against fully-formed dependencies. The host
        /// owns lifetime — disposing the returned ISyncApi (it implements
        /// <see cref="IDisposable"/>) cancels the updates loop.
        /// </summary>
        public static ISyncApi Register(
            IMtProtoRpcPort rpcPort,
            IUpdatesPort updatesPort,
            ISyncStateRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            return Register(rpcPort, updatesPort, repo, bus, logger, clock, channelAccessHashResolver: null);
        }

        /// <summary>
        /// Preferred overload. The
        /// <paramref name="channelAccessHashResolver"/> delegate lets
        /// Sync issue <c>updates.getChannelDifference</c> with the
        /// real <c>access_hash</c> when a gap is detected. Without
        /// it the call carries access_hash=0 and the server replies
        /// CHANNEL_INVALID, leaving the channel cursor stuck and
        /// dropping all subsequent messages from that channel.
        /// Composition wires this delegate to delegate to
        /// <c>IPeerCache.GetChannelAccessHash</c>.
        /// </summary>
        public static ISyncApi Register(
            IMtProtoRpcPort rpcPort,
            IUpdatesPort updatesPort,
            ISyncStateRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            Func<long, long> channelAccessHashResolver)
        {
            if (rpcPort == null) throw new ArgumentNullException("rpcPort");
            if (updatesPort == null) throw new ArgumentNullException("updatesPort");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new SyncApplication(rpcPort, updatesPort, repo, bus, logger, clock, channelAccessHashResolver);
        }

        /// <summary>
        /// Convenience overload using the in-memory repository and the stub
        /// updates port. Composition can use this for smoke tests until the
        /// LocalSettings adapter and real push channel land.
        /// </summary>
        public static ISyncApi RegisterDefault(
            IMtProtoRpcPort rpcPort,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpcPort == null) throw new ArgumentNullException("rpcPort");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return Register(rpcPort, new StubUpdatesPort(), new InMemorySyncStateRepository(), bus, logger, clock);
        }
    }
}
