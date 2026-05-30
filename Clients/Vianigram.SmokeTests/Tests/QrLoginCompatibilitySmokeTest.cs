// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.ValueObjects;

namespace Vianigram.SmokeTests.Tests
{
    public static class QrLoginCompatibilitySmokeTest
    {
        public static Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var entries = new List<TestEntry>();
            entries.Add(RunCase("Telegram QR URI exact Android prefix", ValidateExactAndroidPrefix));
            entries.Add(RunCase("Telegram QR token iOS-compatible base64url round-trip", ValidateBase64UrlRoundTrip));
            return Task.FromResult(entries);
        }

        private static TestEntry RunCase(string name, Action validate)
        {
            try
            {
                validate();
                return new TestEntry
                {
                    Suite = "QrLogin",
                    Name = name,
                    Passed = true,
                    Detail = "OK"
                };
            }
            catch (Exception ex)
            {
                return new TestEntry
                {
                    Suite = "QrLogin",
                    Name = name,
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        private static void ValidateExactAndroidPrefix()
        {
            QrLoginToken token = BuildSampleToken();
            string text = token.QrText;

            if (!text.StartsWith(QrLoginToken.TelegramLoginUriPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("QR text does not start with tg://login?token=");

            if (text.StartsWith("tg://login/?token=", StringComparison.Ordinal))
                throw new InvalidOperationException("QR text contains Uri-normalized slash before query.");

            if (token.TgUri == null)
                throw new InvalidOperationException("TgUri should still be available for non-render callers.");
        }

        private static void ValidateBase64UrlRoundTrip()
        {
            byte[] raw = BuildSampleRawToken();
            QrLoginToken token = QrLoginToken.FromTelegramToken(
                raw,
                new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero));

            string encoded = token.QrText.Substring(QrLoginToken.TelegramLoginUriPrefix.Length);
            if (encoded.IndexOf('+') >= 0 || encoded.IndexOf('/') >= 0)
                throw new InvalidOperationException("Token is not URL-safe base64.");

            int firstPadding = encoded.IndexOf('=');
            if (firstPadding >= 0)
            {
                for (int i = firstPadding; i < encoded.Length; i++)
                {
                    if (encoded[i] != '=')
                        throw new InvalidOperationException("Token has non-suffix base64 padding.");
                }
            }

            if ((encoded.Length % 4) != 0)
                throw new InvalidOperationException("Token should keep iOS-compatible base64 padding.");

            byte[] decoded = DecodeBase64Url(encoded);
            if (decoded.Length != raw.Length)
                throw new InvalidOperationException("Decoded token length mismatch.");

            for (int i = 0; i < raw.Length; i++)
            {
                if (decoded[i] != raw[i])
                    throw new InvalidOperationException("Decoded token byte mismatch at " + i + ".");
            }
        }

        private static QrLoginToken BuildSampleToken()
        {
            return QrLoginToken.FromTelegramToken(
                BuildSampleRawToken(),
                new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero));
        }

        private static byte[] BuildSampleRawToken()
        {
            var bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)((i * 73 + 251) & 0xff);
            }
            return bytes;
        }

        private static byte[] DecodeBase64Url(string text)
        {
            string padded = text.Replace('-', '+').Replace('_', '/');
            int mod = padded.Length % 4;
            if (mod == 2) padded += "==";
            else if (mod == 3) padded += "=";
            else if (mod != 0) throw new FormatException("Invalid base64url length.");
            return Convert.FromBase64String(padded);
        }
    }
}
