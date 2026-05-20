// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// Lifecycle state for a <see cref="Vianigram.SecretChats.Domain.Entities.SecretSession"/>.
    ///
    /// Transitions (initiator side):
    ///   <c>Requesting → Pending → Established → (Renegotiating →) Established → Discarded</c>
    ///
    /// Transitions (responder side):
    ///   <c>Pending → Established → (Renegotiating →) Established → Discarded</c>
    ///
    /// <para>
    /// Mirrors Telegram's <c>encryptedChat*</c> family one-to-one:
    ///   * <c>Requesting</c>      ↔ local has issued <c>messages.requestEncryption</c>; awaiting server ack.
    ///   * <c>Pending</c>         ↔ <c>encryptedChatRequested</c> (incoming) or <c>encryptedChatWaiting</c> (outgoing waiting for peer).
    ///   * <c>Established</c>     ↔ <c>encryptedChat</c> — auth_key derived and fingerprint cross-checked.
    ///   * <c>Renegotiating</c>   ↔ a rekey sub-flow is in progress; sends queue locally.
    ///   * <c>Discarded</c>       ↔ <c>encryptedChatDiscarded</c> — terminal; the auth_key has been wiped.
    /// </para>
    /// </summary>
    public enum SecretSessionState
    {
        /// <summary>Local user has called <c>messages.requestEncryption</c>; awaiting server response.</summary>
        Requesting = 0,
        /// <summary>Server returned <c>encryptedChatWaiting</c> or <c>encryptedChatRequested</c>; DH not finished.</summary>
        Pending = 1,
        /// <summary><c>encryptedChat</c> received with matching key fingerprint; auth_key in vault; ready to send.</summary>
        Established = 2,
        /// <summary>Rekey sub-flow in flight; outbound sends queued, inbound decrypted with the previous key.</summary>
        Renegotiating = 3,
        /// <summary>Terminal. <c>encryptedChatDiscarded</c> received or local discard issued. Key wiped.</summary>
        Discarded = 4
    }
}
