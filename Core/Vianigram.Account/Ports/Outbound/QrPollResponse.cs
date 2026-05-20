// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// QrPollResponse.cs — Vianigram.Account.Ports.Outbound
// Wire DTO for auth.importLoginToken polls (success / two-fa / migrate / expired).

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Coarse classifier emitted by the rpc adapter when decoding an
    /// <c>auth.importLoginToken</c> response.
    /// </summary>
    public enum QrPollKind
    {
        Pending = 0,
        Accepted = 1,
        TwoFaRequired = 2,
        Expired = 3,
        MigrateTo = 4
    }

    /// <summary>
    /// Wire-level response for <c>auth.importLoginToken</c>. Only the fields
    /// the handler currently consumes are projected: a coarse kind plus the
    /// optional 2FA <see cref="PasswordHint"/> and (on
    /// <see cref="QrPollKind.Accepted"/>) the embedded
    /// <c>auth.authorization</c> payload as raw bytes
    /// (<see cref="AuthorizationBytes"/>) so the handler can reuse the
    /// shared <c>TlDecoder.DecodeAuthorization</c> projector to extract the
    /// signed-in user id.
    /// </summary>
    public sealed class QrPollResponse
    {
        public QrPollKind Kind { get; set; }
        public string PasswordHint { get; set; }

        /// <summary>
        /// Raw <c>auth.authorization</c> sub-tree bytes (constructor +
        /// flags + user record …) when <see cref="Kind"/> is
        /// <see cref="QrPollKind.Accepted"/>. Null for other kinds.
        /// </summary>
        public byte[] AuthorizationBytes { get; set; }

        /// <summary>
        /// Server-supplied DC id when <see cref="Kind"/> is
        /// <see cref="QrPollKind.MigrateTo"/>. 0 otherwise.
        /// </summary>
        public int MigrateDcId { get; set; }

        /// <summary>
        /// Server-issued replacement token bytes when <see cref="Kind"/> is
        /// <see cref="QrPollKind.MigrateTo"/>. Null otherwise.
        /// </summary>
        public byte[] MigrateToken { get; set; }
    }
}
