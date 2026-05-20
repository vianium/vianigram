// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// QrLoginPoll.cs — Vianigram.Account.Domain.ValueObjects
// Status snapshot returned when polling QR login via auth.exportLoginToken.

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Outcome of a single QR-login wire round trip.
    /// </summary>
    public enum QrLoginStatus
    {
        Pending = 0,
        Accepted = 1,
        TwoFaRequired = 2,
        Expired = 3,
        SignUpRequired = 4
    }

    /// <summary>
    /// Result of <c>IAccountApi.RequestQrTokenAsync</c> /
    /// <c>IAccountApi.PollQrLoginAsync</c>. Both methods now resolve to the
    /// same wire call (<c>auth.exportLoginToken</c>) and return this rich
    /// status. Field semantics by <see cref="Status"/>:
    ///
    ///   <see cref="QrLoginStatus.Pending"/>      → <see cref="Token"/> populated; render the QR.
    ///   <see cref="QrLoginStatus.Accepted"/>     → <see cref="UserId"/> populated; auth_key persisted.
    ///   <see cref="QrLoginStatus.TwoFaRequired"/>→ <see cref="PasswordHint"/> populated; aggregate transitioned to WaitingForPassword.
    ///   <see cref="QrLoginStatus.Expired"/>      → caller must request a fresh token.
    ///   <see cref="QrLoginStatus.SignUpRequired"/>→ pivot the user to phone sign-up.
    /// </summary>
    public sealed class QrLoginPoll
    {
        public QrLoginStatus Status { get; private set; }
        public string PasswordHint { get; private set; }
        public long UserId { get; private set; }

        /// <summary>
        /// Fresh QR-login token to render. Populated only when
        /// <see cref="Status"/> is <see cref="QrLoginStatus.Pending"/>.
        /// </summary>
        public QrLoginToken Token { get; private set; }

        public QrLoginPoll(QrLoginStatus status, string passwordHint)
            : this(status, passwordHint, 0L, null)
        {
        }

        public QrLoginPoll(QrLoginStatus status, string passwordHint, long userId)
            : this(status, passwordHint, userId, null)
        {
        }

        public QrLoginPoll(QrLoginStatus status, string passwordHint, long userId, QrLoginToken token)
        {
            Status = status;
            PasswordHint = passwordHint ?? string.Empty;
            UserId = userId;
            Token = token;
        }

        public override string ToString()
        {
            return "qr_login_poll(" + Status + (UserId != 0 ? ", user=" + UserId : "") + ")";
        }
    }
}
