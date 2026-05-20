// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// QrLoginToken.cs — Vianigram.Account.Domain.ValueObjects
// QR-login session token returned by auth.exportLoginToken (32-byte hex + tg:// URI + expiry).

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Server-issued QR-login token. <see cref="TokenHex"/> is the lowercase
    /// hex encoding of the 32 raw bytes returned by auth.exportLoginToken;
    /// <see cref="TgUri"/> is the tg://login URI with Telegram's base64url token, suitable for
    /// QR rendering; <see cref="ExpiresAt"/> is the absolute expiry instant
    /// after which the caller must request a fresh token.
    /// </summary>
    public sealed class QrLoginToken
    {
        public string TokenHex { get; private set; }
        public Uri TgUri { get; private set; }
        public DateTimeOffset ExpiresAt { get; private set; }

        public QrLoginToken(string tokenHex, Uri tgUri, DateTimeOffset expiresAt)
        {
            if (tokenHex == null) throw new ArgumentNullException("tokenHex");
            if (tgUri == null) throw new ArgumentNullException("tgUri");
            TokenHex = tokenHex;
            TgUri = tgUri;
            ExpiresAt = expiresAt;
        }

        public override string ToString()
        {
            return "qr_login_token(" + (TokenHex == null ? 0 : TokenHex.Length) + " hex, expires=" + ExpiresAt.ToString("o") + ")";
        }
    }
}
