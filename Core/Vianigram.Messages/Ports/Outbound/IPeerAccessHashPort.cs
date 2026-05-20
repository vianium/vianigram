// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IPeerAccessHashPort.cs — Vianigram.Messages.Ports.Outbound
//
// Outbound port for resolving the (id, access_hash) pair Telegram demands
// in inputUser / inputChannel / inputPeer* arguments. The Messages context
// only knows the peer id (from peerKey strings); the access_hash lives in
// a process-shared cache populated by every typed RPC response that
// carries users:Vector<User> / chats:Vector<Chat>.
//
// Without this port LoadHistoryHandler would write access_hash=0 on the
// wire, and Telegram answers with PEER_ID_INVALID (rapid 72ms failure
// observed in the boot logs).

namespace Vianigram.Messages.Ports.Outbound
{
    /// <summary>
    /// Resolve the cached access_hash for a peer that the Messages handler
    /// is about to address on the wire. Returns 0 when the peer has not
    /// been observed yet — the caller may still attempt the RPC; Telegram
    /// just answers PEER_ID_INVALID, which we surface as a structured error.
    /// </summary>
    public interface IPeerAccessHashPort
    {
        /// <summary>access_hash for a user peer; 0 if unknown.</summary>
        long GetUserAccessHash(long userId);

        /// <summary>access_hash for a channel peer; 0 if unknown.</summary>
        long GetChannelAccessHash(long channelId);
    }
}
