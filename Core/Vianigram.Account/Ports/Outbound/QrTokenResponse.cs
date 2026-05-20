// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// QrTokenResponse.cs — Vianigram.Account.Ports.Outbound
// Wire DTO for auth.exportLoginToken — covers all three outcomes the
// server can return: pending token, migrate-to-dc, or success-with-auth.

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Wire-level response for <c>auth.exportLoginToken</c>. The server
    /// returns one of three TL constructors and we surface them via
    /// <see cref="Kind"/> so the application handler can dispatch without
    /// re-parsing TL bytes:
    ///
    ///   <c>auth.loginToken#629f1980</c>             → <see cref="QrPollKind.Pending"/>
    ///                                                  ( <see cref="Token"/> + <see cref="ExpiresUnixSeconds"/> populated)
    ///   <c>auth.loginTokenSuccess#390d5c5e</c>      → <see cref="QrPollKind.Accepted"/>
    ///                                                  ( <see cref="AuthorizationBytes"/> populated)
    ///   <c>auth.loginTokenMigrateTo#068e9916</c>    → <see cref="QrPollKind.MigrateTo"/>
    ///                                                  ( <see cref="MigrateDcId"/> + <see cref="MigrateToken"/> populated)
    ///   server error <c>SESSION_PASSWORD_NEEDED</c> → <see cref="QrPollKind.TwoFaRequired"/>
    ///
    /// Polling for the unauthenticated client is done by RE-ISSUING
    /// auth.exportLoginToken, not by calling auth.importLoginToken (the
    /// latter is for the already-authorized device to confirm the login
    /// and for switching DCs after MigrateTo). See the QrLoginPageViewModel
    /// for the full flow.
    /// </summary>
    public sealed class QrTokenResponse
    {
        /// <summary>Coarse classifier of the wire response.</summary>
        public QrPollKind Kind { get; set; }

        /// <summary>Raw 32-byte token bytes when <see cref="Kind"/> is <see cref="QrPollKind.Pending"/>.</summary>
        public byte[] Token { get; set; }

        /// <summary>Server-supplied expiry (Unix epoch seconds) when <see cref="Kind"/> is <see cref="QrPollKind.Pending"/>.</summary>
        public int ExpiresUnixSeconds { get; set; }

        /// <summary>
        /// Raw <c>auth.authorization</c> sub-tree bytes (constructor +
        /// flags + user record …) when <see cref="Kind"/> is
        /// <see cref="QrPollKind.Accepted"/>. Null otherwise.
        /// </summary>
        public byte[] AuthorizationBytes { get; set; }

        /// <summary>Server-supplied DC id when <see cref="Kind"/> is <see cref="QrPollKind.MigrateTo"/>; 0 otherwise.</summary>
        public int MigrateDcId { get; set; }

        /// <summary>Server-issued replacement token bytes when <see cref="Kind"/> is <see cref="QrPollKind.MigrateTo"/>; null otherwise.</summary>
        public byte[] MigrateToken { get; set; }
    }
}
