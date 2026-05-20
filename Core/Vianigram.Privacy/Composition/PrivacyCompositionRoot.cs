// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Privacy.Application;
using Vianigram.Privacy.Infrastructure;
using Vianigram.Privacy.Ports.Inbound;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Composition
{
    /// <summary>
    /// Composition root for the Privacy bounded context.
    ///
    /// <para>Wires the in-memory passcode store + stub hasher
    /// and the supplied MTProto RPC adapter into a
    /// <see cref="PrivacyApplication"/> instance, which is the single public
    /// entry point (<see cref="IPrivacyApi"/>).</para>
    ///
    /// <para>Mirrors <c>Vianigram.Search.Composition.SearchCompositionRoot</c>
    /// and <c>Vianigram.Settings.Composition.SettingsCompositionRoot</c>: the
    /// host <c>Vianigram.Composition</c> calls <see cref="Build"/> and stores
    /// the returned <see cref="IPrivacyApi"/> in its own service registry
    /// (e.g. <c>root.Register&lt;IPrivacyApi&gt;(api)</c>).</para>
    ///
    /// <para>The kernel rule that contexts don't reference each other's ports
    /// is upheld: the same <c>IMtProtoRpcPort</c> reference passed in here
    /// implements every per-context interface — it's the same adapter object
    /// surfaced as different types.</para>
    ///
    /// <para><b>Composition boundaries</b>:</para>
    /// <list type="bullet">
    ///   <item><description><c>IPasscodeStore</c> — defaults to <see cref="InMemoryPasscodeStore"/>; the production adapter (DataProtectionProvider + LocalFolder) lives in <c>Vianigram.Storage</c> / the App composition root.</description></item>
    ///   <item><description><c>IPasscodeHasher</c> — defaults to <see cref="StubPasscodeHasher"/>; the PBKDF2-HMAC-SHA512 production adapter wraps the <c>Vianium.Crypto</c> WinMD primitive used by <c>SrpClient</c>.</description></item>
    ///   <item><description><b>Blocked users</b> — NOT wired here. The blocking surface is owned by Vianigram.Contacts and consumed via ACL adapters; the Privacy doc surfaces blocked_users only as a capability.</description></item>
    /// </list>
    /// </summary>
    public static class PrivacyCompositionRoot
    {
        /// <summary>
        /// Build the Privacy application surface and return the inbound API.
        /// The host composition root is responsible for storing the returned
        /// instance in whatever service container it uses.
        /// </summary>
        public static IPrivacyApi Build(
            IMtProtoRpcPort rpc,
            IPasscodeStore store,
            IPasscodeHasher hasher,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (store == null) throw new ArgumentNullException("store");
            if (hasher == null) throw new ArgumentNullException("hasher");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new PrivacyApplication(rpc, store, hasher, bus, logger, clock);
        }

        /// <summary>
        /// Convenience overload that wires the in-memory store + stub
        /// hasher for callers that only need V1 wiring (no Storage adapter
        /// yet, no Crypto WinMD bound).
        /// </summary>
        public static IPrivacyApi Build(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            return new PrivacyApplication(
                rpc,
                new InMemoryPasscodeStore(),
                new StubPasscodeHasher(),
                bus,
                logger,
                clock);
        }
    }
}
