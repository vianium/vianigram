// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;
using System.Text;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Canonical text serializer for the V1 preference-value types. The store
    /// (<see cref="Vianigram.Settings.Ports.Outbound.IPreferencesStore"/>)
    /// exclusively persists strings; this static helper converts to and from
    /// that representation in a deterministic, dependency-free format.
    ///
    /// Format choices:
    ///   * Scalars  — invariant-culture <c>ToString</c> (so <c>1.5</c> never
    ///                becomes <c>1,5</c>).
    ///   * Booleans — <c>true</c> / <c>false</c>.
    ///   * Enums    — invariant string name (<c>System</c>, <c>Light</c>, ...).
    ///   * Composites (LanguagePack, DataUsagePolicy, ChatBackground) —
    ///     pipe-delimited fields with a leading version tag (<c>v1|...</c>) so
    ///     a future schema rev can round-trip without ambiguity.
    ///
    /// We intentionally avoid JSON here: WP8.1 ships no <c>System.Text.Json</c>
    /// and pulling DataContractJsonSerializer for ~150-byte values is overkill.
    /// </summary>
    internal static class PreferenceSerializer
    {
        private const string V1Prefix = "v1|";
        private const char Sep = '|';

        public static string Serialize(object value)
        {
            if (value == null) return string.Empty;

            if (value is bool) return ((bool)value) ? "true" : "false";
            if (value is int) return ((int)value).ToString(CultureInfo.InvariantCulture);
            if (value is long) return ((long)value).ToString(CultureInfo.InvariantCulture);
            if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            if (value is string) return (string)value;

            if (value is Theme) return ((Theme)value).ToString();
            if (value is EmojiSize) return ((EmojiSize)value).ToString();
            if (value is NetworkKind) return ((NetworkKind)value).ToString();

            if (value is MessageTextSize)
            {
                int pts = ((MessageTextSize)value).Points;
                return pts.ToString(CultureInfo.InvariantCulture);
            }

            if (value is LanguagePack) return SerializeLanguagePack((LanguagePack)value);
            if (value is DataUsagePolicy) return SerializeDataUsagePolicy((DataUsagePolicy)value);
            if (value is ChatBackground) return SerializeChatBackground((ChatBackground)value);
            if (value is ProxyConfig) return SerializeProxyConfig((ProxyConfig)value);

            // Fallback: invariant ToString, accepting that round-trip is not
            // guaranteed for unknown types (the caller is responsible for
            // catalog hygiene).
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        public static T Deserialize<T>(string raw, T fallback)
        {
            if (raw == null) return fallback;

            Type t = typeof(T);

            try
            {
                if (t == typeof(bool))
                {
                    if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) return (T)(object)true;
                    if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase)) return (T)(object)false;
                    return fallback;
                }
                if (t == typeof(int))
                {
                    int v;
                    return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                        ? (T)(object)v
                        : fallback;
                }
                if (t == typeof(long))
                {
                    long v;
                    return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                        ? (T)(object)v
                        : fallback;
                }
                if (t == typeof(double))
                {
                    double v;
                    return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                        ? (T)(object)v
                        : fallback;
                }
                if (t == typeof(string)) return (T)(object)raw;

                if (t == typeof(Theme))
                {
                    Theme v;
                    if (TryParseEnum<Theme>(raw, out v)) return (T)(object)v;
                    return fallback;
                }
                if (t == typeof(EmojiSize))
                {
                    EmojiSize v;
                    if (TryParseEnum<EmojiSize>(raw, out v)) return (T)(object)v;
                    return fallback;
                }
                if (t == typeof(NetworkKind))
                {
                    NetworkKind v;
                    if (TryParseEnum<NetworkKind>(raw, out v)) return (T)(object)v;
                    return fallback;
                }
                if (t == typeof(MessageTextSize))
                {
                    int pts;
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out pts))
                        return (T)(object)new MessageTextSize(pts);
                    return fallback;
                }
                if (t == typeof(LanguagePack))
                {
                    LanguagePack v = ParseLanguagePack(raw);
                    return v == null ? fallback : (T)(object)v;
                }
                if (t == typeof(DataUsagePolicy))
                {
                    DataUsagePolicy v = ParseDataUsagePolicy(raw);
                    return v == null ? fallback : (T)(object)v;
                }
                if (t == typeof(ChatBackground))
                {
                    ChatBackground v = ParseChatBackground(raw);
                    return v == null ? fallback : (T)(object)v;
                }
                if (t == typeof(ProxyConfig))
                {
                    ProxyConfig v = ParseProxyConfig(raw);
                    return v == null ? fallback : (T)(object)v;
                }
            }
            catch
            {
                return fallback;
            }
            return fallback;
        }

        // ---- composites -------------------------------------------------------

        private static string SerializeLanguagePack(LanguagePack p)
        {
            var sb = new StringBuilder(V1Prefix.Length + 32);
            sb.Append(V1Prefix);
            sb.Append(Escape(p.LangCode)).Append(Sep);
            sb.Append(p.Version.ToString(CultureInfo.InvariantCulture)).Append(Sep);
            sb.Append(Escape(p.BaseLangCode ?? string.Empty));
            return sb.ToString();
        }

        private static LanguagePack ParseLanguagePack(string raw)
        {
            string body = StripPrefix(raw);
            if (body == null) return null;
            string[] parts = body.Split(Sep);
            if (parts.Length < 3) return null;
            string code = Unescape(parts[0]);
            int version;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version)) return null;
            string baseCode = Unescape(parts[2]);
            if (string.IsNullOrEmpty(code)) return null;
            return new LanguagePack(code, version, string.IsNullOrEmpty(baseCode) ? null : baseCode);
        }

        private static string SerializeDataUsagePolicy(DataUsagePolicy p)
        {
            var sb = new StringBuilder(V1Prefix.Length + 48);
            sb.Append(V1Prefix);
            sb.Append((int)p.Network).Append(Sep);
            sb.Append(p.AutoDownloadPhotos ? '1' : '0').Append(Sep);
            sb.Append(p.AutoDownloadVideos ? '1' : '0').Append(Sep);
            sb.Append(p.AutoDownloadVoice ? '1' : '0').Append(Sep);
            sb.Append(p.AutoDownloadDocuments ? '1' : '0').Append(Sep);
            sb.Append(p.MaxFileSizeBytes.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static DataUsagePolicy ParseDataUsagePolicy(string raw)
        {
            string body = StripPrefix(raw);
            if (body == null) return null;
            string[] parts = body.Split(Sep);
            if (parts.Length < 6) return null;

            int network;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out network)) return null;

            long max;
            if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out max)) return null;

            return new DataUsagePolicy(
                (NetworkKind)network,
                autoDownloadPhotos: parts[1] == "1",
                autoDownloadVideos: parts[2] == "1",
                autoDownloadVoice: parts[3] == "1",
                autoDownloadDocuments: parts[4] == "1",
                maxFileSizeBytes: max);
        }

        private static string SerializeChatBackground(ChatBackground b)
        {
            var sb = new StringBuilder(V1Prefix.Length + 32);
            sb.Append(V1Prefix);
            sb.Append(Escape(b.Id)).Append(Sep);
            sb.Append(b.IsCustomImage ? '1' : '0').Append(Sep);
            sb.Append(Escape(b.ColorHex));
            return sb.ToString();
        }

        private static ChatBackground ParseChatBackground(string raw)
        {
            string body = StripPrefix(raw);
            if (body == null) return null;
            string[] parts = body.Split(Sep);
            if (parts.Length < 3) return null;
            string id = Unescape(parts[0]);
            if (string.IsNullOrEmpty(id)) return null;
            bool custom = parts[1] == "1";
            string color = Unescape(parts[2]);
            return new ChatBackground(id, custom, color);
        }

        private static string SerializeProxyConfig(ProxyConfig p)
        {
            // v1|enabled|host|port|mode|secretHex|fakeTlsDomain|label
            var sb = new StringBuilder(V1Prefix.Length + 96);
            sb.Append(V1Prefix);
            sb.Append(p.Enabled ? '1' : '0').Append(Sep);
            sb.Append(Escape(p.Host ?? string.Empty)).Append(Sep);
            sb.Append(p.Port.ToString(CultureInfo.InvariantCulture)).Append(Sep);
            sb.Append((int)p.Mode).Append(Sep);
            sb.Append(SecretToHex(p.Secret)).Append(Sep);
            sb.Append(Escape(p.FakeTlsDomain ?? string.Empty)).Append(Sep);
            sb.Append(Escape(p.Label ?? string.Empty));
            return sb.ToString();
        }

        private static ProxyConfig ParseProxyConfig(string raw)
        {
            string body = StripPrefix(raw);
            if (body == null) return null;
            string[] parts = body.Split(Sep);
            if (parts.Length < 7) return null;

            bool enabled = parts[0] == "1";
            string host = Unescape(parts[1]);
            int port;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out port)) return null;
            int modeI;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out modeI)) return null;
            if (modeI < 0 || modeI > 2) return null;
            byte[] secret = SecretFromHex(parts[4]);
            string fakeSni = Unescape(parts[5]);
            string label = Unescape(parts[6]);

            // Defensive: an enabled config from disk must satisfy the ctor
            // invariants. If anything's malformed we fall back to Disabled
            // rather than throwing — the settings UI will let the user
            // re-enter a valid descriptor.
            try
            {
                return new ProxyConfig(
                    enabled,
                    host ?? string.Empty,
                    port,
                    secret,
                    (ProxySecretMode)modeI,
                    fakeSni ?? string.Empty,
                    label ?? string.Empty);
            }
            catch
            {
                return ProxyConfig.Disabled;
            }
        }

        private static readonly char[] HexLowerTable = new char[]
        {
            '0','1','2','3','4','5','6','7','8','9','a','b','c','d','e','f'
        };

        private static string SecretToHex(byte[] secret)
        {
            if (secret == null || secret.Length == 0) return string.Empty;
            var sb = new StringBuilder(secret.Length * 2);
            for (int i = 0; i < secret.Length; i++)
            {
                byte b = secret[i];
                sb.Append(HexLowerTable[(b >> 4) & 0x0F]);
                sb.Append(HexLowerTable[b        & 0x0F]);
            }
            return sb.ToString();
        }

        private static byte[] SecretFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new byte[0];
            if ((hex.Length & 1) != 0) return new byte[0];
            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                int hi = HexNibble(hex[i * 2]);
                int lo = HexNibble(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return new byte[0];
                result[i] = (byte)((hi << 4) | lo);
            }
            return result;
        }

        private static int HexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return 10 + (c - 'a');
            if (c >= 'A' && c <= 'F') return 10 + (c - 'A');
            return -1;
        }

        // ---- helpers ----------------------------------------------------------

        private static bool TryParseEnum<TEnum>(string raw, out TEnum value) where TEnum : struct
        {
            try
            {
                value = (TEnum)Enum.Parse(typeof(TEnum), raw, ignoreCase: true);
                return true;
            }
            catch
            {
                value = default(TEnum);
                return false;
            }
        }

        private static string StripPrefix(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (!raw.StartsWith(V1Prefix, StringComparison.Ordinal)) return null;
            return raw.Substring(V1Prefix.Length);
        }

        /// <summary>
        /// Escape pipe and backslash so composite values survive the
        /// pipe-delimited round-trip even when a field contains the separator.
        /// </summary>
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' || ch == Sep) sb.Append('\\');
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < s.Length)
                {
                    sb.Append(s[++i]);
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
