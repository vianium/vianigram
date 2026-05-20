// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading.Tasks;

namespace Vianigram.Sync.Ports.Outbound
{
    /// <summary>
    /// Push-update subscription port. The MTProto transport calls the registered
    /// handler for every Updates-typed payload that arrives outside of an explicit
    /// RPC response (the long-poll / persistent connection delivery path).
    ///
    /// The payload is the raw TL-serialized "Updates" supertype (one of:
    /// updates#74ae4240, updateShort#78d4dec1, updateShortMessage#313bc7f8,
    /// updateShortChatMessage#4d6deea5, updateShortSentMessage#9015e101,
    /// updatesCombined#725b04c3, updatesTooLong#e317af7e). The handler is
    /// responsible for decoding and dispatching to ProcessUpdatesHandler.
    ///
    /// Throughput is unbounded: the handler must be fast and non-blocking. Heavy
    /// work belongs in the <see cref="Vianigram.Sync.Application.Handlers.UpdatesLoopHandler"/>
    /// which runs the apply path on a single dispatcher to preserve ordering.
    /// </summary>
    public interface IUpdatesPort
    {
        /// <summary>
        /// Subscribe to push updates. Disposing the returned token unregisters
        /// the handler. Multiple subscribers are not expected (Sync is the sole
        /// consumer per principle M6) but the port allows it for diagnostics.
        /// </summary>
        IDisposable Subscribe(Func<byte[], Task> handler);
    }
}
