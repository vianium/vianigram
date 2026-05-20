// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.SecretChats.Application;
using Vianigram.SecretChats.Infrastructure;
using Vianigram.SecretChats.Ports.Inbound;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Composition
{
    /// <summary>
    /// Composition root for the SecretChats bounded context.
    ///
    /// <para>Wires the in-memory secret-chat repository, the stub crypto
    /// port, and the ACL-shared MTProto RPC adapter into a
    /// <see cref="SecretChatsApplication"/> instance — the single public
    /// entry point (<see cref="ISecretChatsApi"/>).</para>
    ///
    /// <para>Mirrors the
    /// <c>Vianigram.Contacts.Composition.ContactsCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls <see cref="Build"/>
    /// and stores the returned <see cref="ISecretChatsApi"/> in its own
    /// service registry (e.g.
    /// <c>root.Register&lt;ISecretChatsApi&gt;(api)</c>).</para>
    ///
    /// <para>The kernel rule that contexts don't reference each other's
    /// ports is upheld: the same <c>IMtProtoRpcPort</c> reference passed in
    /// here implements every per-context interface — it's the same adapter
    /// object surfaced as different types.</para>
    ///
    /// <para>Planned: the convenience overload that constructs the
    /// repository / crypto port internally will be replaced with explicit
    /// <c>Vianigram.Storage</c>-backed and
    /// <c>Vianigram.Core.Crypto</c>-backed adapters; the primary
    /// <see cref="Build"/> overload stays as the canonical injection
    /// point.</para>
    /// </summary>
    public static class SecretChatsCompositionRoot
    {
        /// <summary>
        /// Builds the SecretChats application surface and returns the
        /// inbound API. The host composition root is responsible for
        /// storing the returned instance in whatever service container it
        /// uses.
        /// </summary>
        public static ISecretChatsApi Build(
            IMtProtoRpcPort rpc,
            ISecretCryptoPort crypto,
            ISecretChatRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new SecretChatsApplication(rpc, crypto, repo, bus, logger, clock);
        }

        /// <summary>
        /// Convenience overload that builds the in-memory repository and
        /// the stub crypto port for callers that only need the default
        /// wiring (no SQLite repository, no native crypto WinMD yet).
        /// LOGS A WARNING via <see cref="StubSecretCryptoPort"/> on
        /// construction to make it obvious in production builds.
        /// </summary>
        public static ISecretChatsApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new SecretChatsApplication(
                rpc,
                new StubSecretCryptoPort(logger),
                new InMemorySecretChatRepository(),
                bus,
                logger,
                clock);
        }
    }
}
