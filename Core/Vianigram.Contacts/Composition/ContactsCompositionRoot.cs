// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Contacts.Application;
using Vianigram.Contacts.Infrastructure;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Contacts.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;

namespace Vianigram.Contacts.Composition
{
    /// <summary>
    /// Composition root for the Contacts bounded context.
    ///
    /// Wires the in-memory contact repository (V1) and the ACL-shared MTProto
    /// RPC adapter into a <see cref="ContactsApplication"/> instance, which is
    /// the single public entry point (<see cref="IContactsApi"/>).
    ///
    /// Mirrors the <c>Vianigram.Chats.Composition.ChatsCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls <see cref="Build"/>
    /// and stores the returned <see cref="IContactsApi"/> in its own service
    /// registry (e.g. <c>root.Register&lt;IContactsApi&gt;(api)</c>).
    ///
    /// The kernel rule that contexts don't reference each other's ports is
    /// upheld: the same <c>IMtProtoRpcPort</c> reference passed in here
    /// implements every per-context interface — it's the same adapter object
    /// surfaced as different types.
    /// </summary>
    public static class ContactsCompositionRoot
    {
        /// <summary>
        /// Builds the Contacts application surface and returns the inbound
        /// API. The host composition root is responsible for storing the
        /// returned instance in whatever service container it uses.
        /// </summary>
        public static IContactsApi Build(
            IMtProtoRpcPort rpc,
            IContactRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new ContactsApplication(rpc, repo, bus, logger, clock);
        }

        /// <summary>
        /// Convenience overload that builds the in-memory repository for
        /// callers that only need the V1 wiring (no SQLite adapter yet).
        /// </summary>
        public static IContactsApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new ContactsApplication(rpc, new InMemoryContactRepository(), bus, logger, clock);
        }
    }
}
