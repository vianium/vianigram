// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Calls.Application;
using Vianigram.Calls.Infrastructure;
using Vianigram.Calls.Ports.Inbound;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;

namespace Vianigram.Calls.Composition
{
    /// <summary>
    /// Composition root for the Calls bounded context.
    ///
    /// <para>Wires the call repository, the VoIP/crypto outbound ports,
    /// and the ACL-shared MTProto RPC adapter into a
    /// <see cref="CallsApplication"/> instance — the single public entry
    /// point (<see cref="ICallsApi"/>).</para>
    ///
    /// <para>Mirrors the
    /// <c>Vianigram.SecretChats.Composition.SecretChatsCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls
    /// <see cref="Build"/> and stores the returned
    /// <see cref="ICallsApi"/> in its own service registry (e.g.
    /// <c>root.Register&lt;ICallsApi&gt;(api)</c>).</para>
    ///
    /// <para>The kernel rule that contexts don't reference each other's
    /// ports is upheld: the same <c>IMtProtoRpcPort</c> reference passed
    /// in here implements every per-context interface — the same adapter
    /// object surfaced as different types.</para>
    ///
    /// <para>The production host is expected to pass the
    /// <c>VianiumVoIP</c>-backed adapter from Composition. The
    /// fallback overloads keep the context testable and fail fast until a
    /// native media runtime is wired.</para>
    /// </summary>
    public static class CallsCompositionRoot
    {
        /// <summary>
        /// Builds the Calls application surface and returns the inbound
        /// API. The host composition root is responsible for storing the
        /// returned instance in whatever service container it uses.
        /// </summary>
        public static ICallsApi Build(
            IMtProtoRpcPort rpc,
            ICallCryptoVault crypto,
            IVoipMediaPort voip,
            ICallRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IUserAccessHashPort userHashes)
        {
            return Build(rpc, crypto, voip, repo, bus, logger, clock, userHashes, new CallSignalingRpcPort(rpc, logger));
        }

        public static ICallsApi Build(
            IMtProtoRpcPort rpc,
            ICallCryptoVault crypto,
            IVoipMediaPort voip,
            ICallRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IUserAccessHashPort userHashes,
            ICallSignalingRpcPort signalingRpc)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (voip == null) throw new ArgumentNullException("voip");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new CallsApplication(rpc, crypto, voip, repo, bus, logger, clock, userHashes, signalingRpc);
        }

        /// <summary>Legacy 6-arg overload (no userHashes — phone.requestCall
        /// will use accessHash=0 and the server returns USER_ID_INVALID).
        /// Retained for tests and legacy callers; production wiring should
        /// use the userHashes overload below.</summary>
        public static ICallsApi Build(
            IMtProtoRpcPort rpc,
            IVoipMediaPort voip,
            ICallRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            return Build(rpc, new StubCallCryptoVault(), voip, repo, bus, logger, clock, null);
        }

        /// <summary>Legacy 4-arg overload — see comment on the legacy 6-arg
        /// overload above.</summary>
        public static ICallsApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            return Build(rpc, bus, logger, clock, null);
        }

        /// <summary>
        /// Convenience overload that builds the in-memory repository and
        /// the stub VoIP port for tests or shells that do not provide the
        /// native VoIP adapter. LOGS A WARNING via <see cref="StubVoipMediaPort"/> on
        /// construction to make it obvious in production builds.
        /// </summary>
        public static ICallsApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IUserAccessHashPort userHashes)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            var crypto = new StubCallCryptoVault();
            var voip = new StubVoipMediaPort(logger);
            return new CallsApplication(
                rpc,
                crypto,
                voip,
                new InMemoryCallRepository(),
                bus,
                logger,
                clock,
                userHashes);
        }
    }
}
