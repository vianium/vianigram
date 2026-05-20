// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;
using System.Text;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Parses the user-typeable MTProxy descriptors the settings UI
    /// accepts. Supported input shapes:
    ///
    ///   1. Full <c>tg://proxy?server=X&amp;port=Y&amp;secret=Z</c> URL —
    ///      Telegram's canonical share format (also reachable from the
    ///      proxy bot's "Use this proxy" button).
    ///
    ///   2. Full <c>https://t.me/proxy?server=X&amp;port=Y&amp;secret=Z</c>
    ///      URL — the public-web mirror of the same descriptor.
    ///
    ///   3. Raw hex string (32 / 34 hex chars). 32 = legacy raw 16
    ///      bytes; 34 = mode-prefixed (<c>dd...</c> or <c>ee...</c>).
    ///
    ///   4. Raw URL-safe base64 of the same payload (sometimes shared
    ///      on forums as the "compact" form).
    ///
    /// The parser never raises — every malformed input returns
    /// <c>false</c> from <see cref="TryParse"/>. On success the returned
    /// <see cref="ProxyConfig"/> is fully validated (host non-empty,
    /// port in (0,65535], 16-byte secret, FakeTls implies SNI present).
    /// </summary>
    public static class ProxyConfigParser
    {
        /// <summary>
        /// Try to parse <paramref name="input"/> as an MTProxy descriptor.
        /// The boolean <paramref name="enabled"/> overlays the result —
        /// pass <c>true</c> to immediately arm the parsed proxy or
        /// <c>false</c> to stage it as a candidate the user can flip on.
        /// </summary>
        public static bool TryParse(string input, bool enabled, out ProxyConfig config)
        {
            config = null;
            if (string.IsNullOrEmpty(input)) return false;

            string trimmed = input.Trim();

            // 1+2. URL forms.
            if (LooksLikeUrl(trimmed))
            {
                return TryParseUrl(trimmed, enabled, out config);
            }

            // 3. Bare secret — host & port are unknown; reject because
            //    you can't dial a proxy without them. The settings UI
            //    routes bare secrets into the Secret text box and lets
            //    the user fill host / port separately.
            return false;
        }

        /// <summary>
        /// Decompose a raw hex/base64 MTProxy secret into its (16-byte payload,
        /// mode, fake-TLS domain) triple. The settings UI uses this on the
        /// secret-only text box so the user doesn't have to manually pick
        /// between Legacy/Secure/FakeTls — the input format determines it.
        /// </summary>
        public static bool TryParseSecret(
            string secret, out byte[] payload, out ProxySecretMode mode, out string fakeTlsDomain)
        {
            payload = null;
            mode = ProxySecretMode.Legacy;
            fakeTlsDomain = string.Empty;
            if (string.IsNullOrEmpty(secret)) return false;

            byte[] raw = DecodeSecretBlob(secret.Trim());
            if (raw == null || raw.Length < 16) return false;

            return ClassifySecretBlob(raw, out payload, out mode, out fakeTlsDomain);
        }

        // -----------------------------------------------------------------
        // URL parser
        // -----------------------------------------------------------------

        private static bool LooksLikeUrl(string s)
        {
            return s.StartsWith("tg://", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("https://t.me/", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("http://t.me/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryParseUrl(string url, bool enabled, out ProxyConfig config)
        {
            config = null;

            // Split out the query string. tg://proxy?server=...&port=...&secret=...
            int q = url.IndexOf('?');
            if (q < 0 || q == url.Length - 1) return false;

            string scheme = url.Substring(0, q);
            string query = url.Substring(q + 1);

            // The path part has to be /proxy (or just "proxy" for the tg://
            // form). Reject other tg:// links to avoid mis-parsing user input.
            string pathLower = scheme.ToLowerInvariant();
            if (pathLower != "tg://proxy"
                && pathLower != "https://t.me/proxy"
                && pathLower != "http://t.me/proxy")
            {
                return false;
            }

            string server = null;
            string portStr = null;
            string secretStr = null;

            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                string key = pair.Substring(0, eq);
                string val = pair.Substring(eq + 1);
                string decoded = UrlDecode(val);
                if (string.Equals(key, "server", StringComparison.OrdinalIgnoreCase)) server = decoded;
                else if (string.Equals(key, "port",   StringComparison.OrdinalIgnoreCase)) portStr = decoded;
                else if (string.Equals(key, "secret", StringComparison.OrdinalIgnoreCase)) secretStr = decoded;
            }

            if (string.IsNullOrEmpty(server)) return false;
            if (string.IsNullOrEmpty(portStr)) return false;
            if (string.IsNullOrEmpty(secretStr)) return false;

            int port;
            if (!int.TryParse(portStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out port))
                return false;
            if (port <= 0 || port > 65535) return false;

            byte[] payload;
            ProxySecretMode mode;
            string fakeSni;
            if (!TryParseSecret(secretStr, out payload, out mode, out fakeSni)) return false;

            try
            {
                config = new ProxyConfig(
                    enabled,
                    server,
                    port,
                    payload,
                    mode,
                    fakeSni ?? string.Empty,
                    /* label = */ string.Empty);
                return true;
            }
            catch
            {
                config = null;
                return false;
            }
        }

        // -----------------------------------------------------------------
        // Secret-blob decoding (hex OR url-safe base64) + mode classifier
        // -----------------------------------------------------------------

        private static byte[] DecodeSecretBlob(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            byte[] viaHex = TryHex(s);
            if (viaHex != null) return viaHex;
            byte[] viaBase64 = TryBase64Url(s);
            return viaBase64;
        }

        private static byte[] TryHex(string s)
        {
            if ((s.Length & 1) != 0) return null;
            byte[] result = new byte[s.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexNibble(s[i * 2]);
                int lo = HexNibble(s[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                result[i] = (byte)((hi << 4) | lo);
            }
            return result;
        }

        private static byte[] TryBase64Url(string s)
        {
            // URL-safe alphabet: '-' → '+', '_' → '/', no padding.
            try
            {
                StringBuilder sb = new StringBuilder(s.Length + 4);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '-') sb.Append('+');
                    else if (c == '_') sb.Append('/');
                    else if (c == '=') sb.Append('=');
                    else sb.Append(c);
                }
                while (sb.Length % 4 != 0) sb.Append('=');
                return Convert.FromBase64String(sb.ToString());
            }
            catch
            {
                return null;
            }
        }

        private static int HexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
            if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
            return -1;
        }

        /// <summary>
        /// Inspect the raw decoded blob and decide between Legacy / Secure /
        /// FakeTls. Returns the 16-byte payload and (for FakeTls) the trailing
        /// ASCII SNI.
        ///
        ///   * 16 bytes  → Legacy (no flag byte).
        ///   * 17 bytes  with leading 0xDD → Secure.
        ///   * 17+ bytes with leading 0xEE → FakeTls + N-byte ASCII SNI.
        /// </summary>
        private static bool ClassifySecretBlob(
            byte[] raw, out byte[] payload, out ProxySecretMode mode, out string fakeTlsDomain)
        {
            payload = null;
            mode = ProxySecretMode.Legacy;
            fakeTlsDomain = string.Empty;

            if (raw == null) return false;
            if (raw.Length == 16)
            {
                payload = raw;
                mode = ProxySecretMode.Legacy;
                return true;
            }
            if (raw.Length >= 17)
            {
                byte flag = raw[0];
                if (flag == 0xDD && raw.Length == 17)
                {
                    payload = SliceMid(raw, 1, 16);
                    mode = ProxySecretMode.Secure;
                    return true;
                }
                if (flag == 0xEE && raw.Length > 17)
                {
                    payload = SliceMid(raw, 1, 16);
                    int sniLen = raw.Length - 17;
                    var sb = new StringBuilder(sniLen);
                    for (int i = 0; i < sniLen; i++)
                    {
                        byte b = raw[17 + i];
                        // The FakeTLS SNI is ASCII; reject non-ASCII bytes
                        // rather than silently producing mojibake.
                        if (b == 0 || b > 0x7F) return false;
                        sb.Append((char)b);
                    }
                    fakeTlsDomain = sb.ToString();
                    mode = ProxySecretMode.FakeTls;
                    return true;
                }
            }
            return false;
        }

        private static byte[] SliceMid(byte[] src, int offset, int length)
        {
            byte[] result = new byte[length];
            Array.Copy(src, offset, result, 0, length);
            return result;
        }

        // -----------------------------------------------------------------
        // %-decoding (RFC 3986 percent-encoding, ASCII subset is enough)
        // -----------------------------------------------------------------

        private static string UrlDecode(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf('%') < 0 && s.IndexOf('+') < 0) return s;

            byte[] buf = new byte[s.Length];
            int len = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '+')
                {
                    buf[len++] = (byte)' ';
                }
                else if (c == '%' && i + 2 < s.Length)
                {
                    int hi = HexNibble(s[i + 1]);
                    int lo = HexNibble(s[i + 2]);
                    if (hi >= 0 && lo >= 0)
                    {
                        buf[len++] = (byte)((hi << 4) | lo);
                        i += 2;
                    }
                    else
                    {
                        buf[len++] = (byte)c;
                    }
                }
                else
                {
                    // We pass ASCII through directly. UTF-8 multibyte in
                    // proxy URLs is exceedingly rare; if it shows up the
                    // round-trip is still byte-clean since we encode each
                    // char value as a uint8.
                    buf[len++] = (byte)c;
                }
            }
            return Encoding.UTF8.GetString(buf, 0, len);
        }
    }
}
