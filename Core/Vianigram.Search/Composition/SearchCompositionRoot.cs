// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Search.Application;
using Vianigram.Search.Infrastructure;
using Vianigram.Search.Ports.Inbound;
using Vianigram.Search.Ports.Outbound;

namespace Vianigram.Search.Composition
{
    /// <summary>
    /// Composition root for the Search bounded context.
    ///
    /// Wires the in-memory search history and the supplied
    /// MTProto RPC adapter into a <see cref="SearchApplication"/> instance,
    /// which is the single public entry point (<see cref="ISearchApi"/>).
    ///
    /// Mirrors the
    /// <c>Vianigram.Settings.Composition.SettingsCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls <see cref="Build"/>
    /// and stores the returned <see cref="ISearchApi"/> in its own service
    /// registry (e.g. <c>root.Register&lt;ISearchApi&gt;(api)</c>).
    ///
    /// The kernel rule that contexts don't reference each other's ports is
    /// upheld: the same <c>IMtProtoRpcPort</c> reference passed in here
    /// implements every per-context interface — it's the same adapter object
    /// surfaced as different types.
    /// </summary>
    public static class SearchCompositionRoot
    {
        /// <summary>
        /// Builds the Search application surface and returns the inbound API.
        /// The host composition root is responsible for storing the returned
        /// instance in whatever service container it uses.
        /// </summary>
        public static ISearchApi Build(
            IMtProtoRpcPort rpc,
            ISearchHistory history,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (history == null) throw new ArgumentNullException("history");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new SearchApplication(rpc, history, bus, logger, clock);
        }

        /// <summary>
        /// Convenience overload that builds the in-memory search history for
        /// callers that only need the V1 wiring (no Storage adapter / no real
        /// LocalSettings sink yet).
        /// </summary>
        public static ISearchApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new SearchApplication(rpc, new InMemorySearchHistory(), bus, logger, clock);
        }
    }
}
