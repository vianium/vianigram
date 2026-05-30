// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// QrLoginToken.cs — Vianigram.Account.Domain.ValueObjects
// QR-login session token returned by auth.exportLoginToken.

using System;
using System.Globalization;
using System.Text;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Server-issued QR-login token. <see cref="TokenHex"/> is the lowercase
    /// hex encoding of the raw bytes returned by auth.exportLoginToken;
    /// <see cref="QrText"/> is the exact tg://login deep link to render in
    /// the QR code; <see cref="ExpiresAt"/> is the absolute expiry instant
    /// after which the caller must request a fresh token.
    /// </summary>
    public sealed class QrLoginToken
    {
        public const string TelegramLoginUriPrefix = "tg://login?token=";

        public string TokenHex { get; private set; }
        public Uri TgUri { get; private set; }
        public string QrText { get; private set; }
        public DateTimeOffset ExpiresAt { get; private set; }

        public QrLoginToken(string tokenHex, Uri tgUri, string qrText, DateTimeOffset expiresAt)
        {
            if (tokenHex == null) throw new ArgumentNullException("tokenHex");
            if (tgUri == null) throw new ArgumentNullException("tgUri");
            if (qrText == null) throw new ArgumentNullException("qrText");
            if (!qrText.StartsWith(TelegramLoginUriPrefix, StringComparison.Ordinal))
                throw new ArgumentException("QR login text must use Telegram's exact tg://login?token= prefix", "qrText");
            TokenHex = tokenHex;
            TgUri = tgUri;
            QrText = qrText;
            ExpiresAt = expiresAt;
        }

        public static QrLoginToken FromTelegramToken(byte[] token, DateTimeOffset expiresAt)
        {
            if (token == null) throw new ArgumentNullException("token");
            if (token.Length == 0) throw new ArgumentException("QR token is empty", "token");

            string qrText = TelegramLoginUriPrefix + ToTelegramQrToken(token);
            return new QrLoginToken(ToLowerHex(token), new Uri(qrText), qrText, expiresAt);
        }

        public static string ToTelegramQrToken(byte[] token)
        {
            if (token == null) throw new ArgumentNullException("token");
            if (token.Length == 0) throw new ArgumentException("QR token is empty", "token");

            // Telegram iOS generates auth-transfer QR tokens with URL-safe
            // base64 while keeping the standard '=' padding. Android accepts
            // this padded form through Base64.URL_SAFE.
            return Convert.ToBase64String(token)
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public override string ToString()
        {
            return "qr_login_token(" + (TokenHex == null ? 0 : TokenHex.Length) + " hex, expires=" + ExpiresAt.ToString("o") + ")";
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
