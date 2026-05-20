// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ChatsCompositionRoot.cs — Vianigram.Chats.Composition
// Single entry point for assembling the Chats bounded context.

using System;
using Vianigram.Chats.Application;
using Vianigram.Chats.Infrastructure;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;

namespace Vianigram.Chats.Composition
{
    /// <summary>
    /// Composition root for the Chats bounded context.
    ///
    /// Wires the in-memory dialog repository (V1) and the ACL-shared MTProto RPC
    /// adapter into a <see cref="ChatsApplication"/> instance, which is the single
    /// public entry point (<see cref="IChatsApi"/>).
    ///
    /// The host <c>VianigramCompositionRoot</c> is referenced loosely as <see cref="object"/>
    /// to avoid pulling Vianigram.Composition into the Chats csproj. The host calls
    /// <see cref="Build(IMtProtoRpcPort, IEventBus)"/> and stores the returned <see cref="IChatsApi"/>
    /// in its own service registry (`root.Register&lt;IChatsApi&gt;(api)` in the host).
    ///
    /// The kernel rule that contexts don't reference each other's ports is upheld:
    /// the same <c>IMtProtoRpcPort</c> reference passed in here implements every
    /// per-context interface — it's the same adapter object surfaced as different types.
    /// </summary>
    public static class ChatsCompositionRoot
    {
        /// <summary>
        /// Builds the Chats application surface and returns the inbound API. The host
        /// composition root is responsible for registering the returned instance in
        /// whatever service container it uses.
        ///
        /// Logger defaults to <see cref="DebugLogger"/> for hosts that do not yet
        /// thread an <see cref="ILogger"/>; handlers all attach a
        /// <see cref="TimestampedLogger"/> with the <c>Chats.&lt;Method&gt;</c>
        /// component name on top.
        /// </summary>
        public static IChatsApi Build(IMtProtoRpcPort rpcAdapter, IEventBus bus)
        {
            return Build(rpcAdapter, bus, new DebugLogger());
        }

        /// <summary>
        /// Logger-aware overload. Preferred for hosts that have a configured
        /// <see cref="ILogger"/> available at composition time.
        /// </summary>
        public static IChatsApi Build(IMtProtoRpcPort rpcAdapter, IEventBus bus, ILogger logger)
        {
            if (rpcAdapter == null) throw new ArgumentNullException("rpcAdapter");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");

            var dialogRepo = new InMemoryDialogRepository();
            var app = new ChatsApplication(dialogRepo, rpcAdapter, bus, logger);
            return app;
        }

        /// <summary>
        /// Convenience overload that lets callers also inject a custom repository
        /// (e.g., a SQLite-backed adapter once Vianigram.Storage lands).
        /// </summary>
        public static IChatsApi Build(IDialogRepository repo, IMtProtoRpcPort rpcAdapter, IEventBus bus)
        {
            return Build(repo, rpcAdapter, bus, new DebugLogger());
        }

        /// <summary>
        /// Repository + logger overload.
        /// </summary>
        public static IChatsApi Build(IDialogRepository repo, IMtProtoRpcPort rpcAdapter, IEventBus bus, ILogger logger)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpcAdapter == null) throw new ArgumentNullException("rpcAdapter");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");

            var app = new ChatsApplication(repo, rpcAdapter, bus, logger);
            return app;
        }
    }
}
