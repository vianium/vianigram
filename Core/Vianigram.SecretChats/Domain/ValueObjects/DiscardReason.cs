// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// Why a secret-chat session ended. Carried by
    /// <see cref="Vianigram.SecretChats.Domain.Events.SecretChatDiscarded"/>.
    /// </summary>
    public enum DiscardReason
    {
        /// <summary>Local user discarded via <c>messages.discardEncryption</c>.</summary>
        LocalUserDiscarded = 0,
        /// <summary>Peer discarded — observed via <c>updateEncryption</c> with <c>encryptedChatDiscarded</c>.</summary>
        PeerDiscarded = 1,
        /// <summary>DH key fingerprint cross-check failed — security abort.</summary>
        FingerprintMismatch = 2,
        /// <summary>Local logout / account switch wipes all sessions for that account.</summary>
        LocalLogout = 3,
        /// <summary>Protocol error during DH negotiation; session never reached <c>Established</c>.</summary>
        ProtocolError = 4
    }
}
