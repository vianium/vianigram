// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Stickers.Application;
using Vianigram.Stickers.Infrastructure;
using Vianigram.Stickers.Ports.Inbound;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Composition
{
    /// <summary>
    /// Composition root for the Stickers bounded context.
    ///
    /// Wires the in-memory repository + cache and the
    /// ACL-shared MTProto RPC adapter into a <see cref="StickersApplication"/>
    /// instance, which is the single public entry point
    /// (<see cref="IStickersApi"/>).
    ///
    /// Mirrors the <c>Vianigram.Contacts.Composition.ContactsCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls <see cref="Build"/>
    /// and stores the returned <see cref="IStickersApi"/> in its own service
    /// registry (e.g. <c>root.Register&lt;IStickersApi&gt;(api)</c>).
    ///
    /// The kernel rule that contexts don't reference each other's ports is
    /// upheld: the same <c>IMtProtoRpcPort</c> reference passed in here
    /// implements every per-context interface — it's the same adapter object
    /// surfaced as different types.
    /// </summary>
    public static class StickersCompositionRoot
    {
        /// <summary>
        /// Builds the Stickers application surface and returns the inbound
        /// API. The host composition root is responsible for storing the
        /// returned instance in whatever service container it uses.
        /// </summary>
        public static IStickersApi Build(
            IMtProtoRpcPort rpc,
            IStickerRepository repo,
            IStickerCachePort cache,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (repo == null) throw new ArgumentNullException("repo");
            if (cache == null) throw new ArgumentNullException("cache");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new StickersApplication(rpc, repo, cache, bus, logger, clock);
        }

        /// <summary>
        /// Convenience overload that builds the in-memory repository and cache
        /// for callers that only need the V1 wiring (no Storage adapter yet).
        /// </summary>
        public static IStickersApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new StickersApplication(
                rpc,
                new InMemoryStickerRepository(),
                new InMemoryStickerCache(),
                bus,
                logger,
                clock);
        }
    }
}
