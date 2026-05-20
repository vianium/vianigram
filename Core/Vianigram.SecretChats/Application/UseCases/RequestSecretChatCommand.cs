// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.SecretChats.Application.UseCases
{
    /// <summary>
    /// Initiate a new secret chat with <c>peer_user_id</c>. Triggers
    /// generation of <c>a</c>, computation of <c>g_a</c>, and the
    /// <c>messages.requestEncryption</c> RPC.
    /// </summary>
    public sealed class RequestSecretChatCommand
    {
        public long PeerUserId { get; private set; }
        public long PeerAccessHash { get; private set; }

        public RequestSecretChatCommand(long peerUserId, long peerAccessHash)
        {
            PeerUserId = peerUserId;
            PeerAccessHash = peerAccessHash;
        }
    }
}
