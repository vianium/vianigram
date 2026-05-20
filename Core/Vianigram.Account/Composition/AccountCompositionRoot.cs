// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Application;
using Vianigram.Account.Ports.Inbound;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;

namespace Vianigram.Account.Composition
{
    /// <summary>
    /// Composition root for the Account bounded context.
    ///
    /// Wires the outbound ports (<see cref="IMtProtoRpcPort"/>,
    /// <see cref="IAuthKeyStore"/>, <see cref="IAuthKeyGeneratorPort"/>,
    /// <see cref="ISrpClientPort"/>) plus the kernel infrastructure
    /// (<see cref="IEventBus"/>, <see cref="ILogger"/>, <see cref="IClock"/>)
    /// into a single <see cref="AccountApplication"/> instance, which is the
    /// single public entry point exposed as <see cref="IAccountApi"/>.
    ///
    /// Mirrors the <c>Vianigram.Chats.Composition.ChatsCompositionRoot</c>
    /// pattern: the host <c>Vianigram.Composition</c> calls
    /// <see cref="Register"/> and stores the returned interface in its own
    /// service registry. The kernel rule that bounded contexts do not reference
    /// each other's ports is upheld — the same concrete adapter passed in here
    /// implements the per-context outbound interfaces.
    /// </summary>
    public static class AccountCompositionRoot
    {
        /// <summary>
        /// Builds the Account application surface and returns the inbound API.
        /// </summary>
        public static IAccountApi Register(
            IMtProtoRpcPort rpcPort,
            IAuthKeyStore keyStore,
            IAuthKeyGeneratorPort keyGen,
            ISrpClientPort srp,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpcPort == null) throw new ArgumentNullException("rpcPort");
            if (keyStore == null) throw new ArgumentNullException("keyStore");
            if (keyGen == null) throw new ArgumentNullException("keyGen");
            if (srp == null) throw new ArgumentNullException("srp");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            var app = new AccountApplication(rpcPort, keyStore, keyGen, srp, bus, logger, clock);
            return app;
        }

        /// <summary>
        /// Builds the Account application surface with real Telegram
        /// application credentials for auth.sendCode / auth.exportLoginToken.
        /// </summary>
        public static IAccountApi Register(
            IMtProtoRpcPort rpcPort,
            IAuthKeyStore keyStore,
            IAuthKeyGeneratorPort keyGen,
            ISrpClientPort srp,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            int apiId,
            string apiHash,
            int activeDcId,
            IPreferredDcStore preferredDcStore = null)
        {
            if (rpcPort == null) throw new ArgumentNullException("rpcPort");
            if (keyStore == null) throw new ArgumentNullException("keyStore");
            if (keyGen == null) throw new ArgumentNullException("keyGen");
            if (srp == null) throw new ArgumentNullException("srp");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");
            if (apiHash == null) throw new ArgumentNullException("apiHash");

            var app = new AccountApplication(
                rpcPort,
                keyStore,
                keyGen,
                srp,
                bus,
                logger,
                clock,
                NullTelemetry.Instance,
                apiId,
                apiHash,
                activeDcId,
                preferredDcStore);
            return app;
        }
    }
}
