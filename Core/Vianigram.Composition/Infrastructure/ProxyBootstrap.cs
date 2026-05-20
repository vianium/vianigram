// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ProxyBootstrap.cs
//
// On-launch synchronous read of the saved MTProxy descriptor +
// arming of the native Vianigram.MTProto runtime. Runs once during
// BuildPhase2Async BEFORE the MTProto channel opens, so the first
// dial of the session goes through the proxy if one is configured.
//
// We read LocalSettings directly (not via the IPreferencesStore
// abstraction) for two reasons:
//   1. LocalSettings reads are synchronous; we don't need the async
//      ceremony of IPreferencesStore on the cold-launch critical path.
//   2. The IPreferencesStore is built LATER in BuildPhase2Async
//      (during the Settings ctx phase, after the MTProto channel
//      has already opened). Going through it would invert the
//      bootstrap order and the first channel would dial direct.
//
// Round-trip wire format MUST match
// Vianigram.Settings.Infrastructure.PreferenceSerializer
// — that's the same serializer the runtime ProxySettingsPage save
// path uses, so the persisted state survives a launch via either
// route.

using System;
using Vianigram.Kernel.Logging;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Infrastructure;
using Windows.Storage;

namespace Vianigram.Composition.Infrastructure
{
    public static class ProxyBootstrap
    {
        // Must match Vianigram.Settings.Infrastructure.PreferenceKeys.ProxyMtProto.Name.
        private const string ProxyKey = "network.proxy.mtproto";

        /// <summary>
        /// Reads the saved <see cref="ProxyConfig"/> from LocalSettings and,
        /// if enabled, arms the native Vianigram.MTProto.MtProxyRuntime.
        /// Failures are logged but never thrown — a misconfigured proxy
        /// must NEVER block app launch.
        /// </summary>
        public static void LoadAndApply(ILogger logger)
        {
            IComponentLogger log = new TimestampedLogger(logger, "MtProxy.Bootstrap");
            try
            {
                object boxed;
                if (!ApplicationData.Current.LocalSettings.Values.TryGetValue(ProxyKey, out boxed) || boxed == null)
                {
                    log.Info("no saved MTProxy descriptor — direct dial");
                    SafeClear(log);
                    return;
                }
                string raw = boxed as string;
                if (string.IsNullOrEmpty(raw))
                {
                    SafeClear(log);
                    return;
                }

                // The PreferenceSerializer is internal; we can't reach it
                // from another assembly. Use ProxyConfigParser-equivalent
                // logic via the public TryParseProxyConfig helper.
                ProxyConfig cfg = ParsePersisted(raw);
                if (cfg == null || !cfg.Enabled)
                {
                    SafeClear(log);
                    return;
                }

                bool ok = Vianigram.MTProto.MtProxyRuntime.SetActiveProxy(
                    cfg.Host,
                    cfg.Port,
                    cfg.Secret,
                    (int)cfg.Mode,
                    cfg.FakeTlsDomain);
                if (ok)
                {
                    log.Info("MTProxy armed at boot: " + cfg.Host + ":" + cfg.Port + " mode=" + cfg.Mode);
                }
                else
                {
                    log.Warn("MtProxyRuntime.SetActiveProxy rejected the saved descriptor — direct dial");
                }
            }
            catch (Exception ex)
            {
                log.Warn("MTProxy bootstrap threw: " + ex.GetType().Name + ": " + ex.Message);
                SafeClear(log);
            }
        }

        private static void SafeClear(IComponentLogger log)
        {
            try { Vianigram.MTProto.MtProxyRuntime.ClearActiveProxy(); }
            catch (Exception ex)
            {
                log.Warn("MtProxyRuntime.ClearActiveProxy threw: " + ex.Message);
            }
        }

        // Inline parser for the v1 PreferenceSerializer format. We keep
        // it isolated to this assembly so the Composition layer doesn't
        // grow a friend dependency on Vianigram.Settings.Infrastructure
        // internals.
        //
        // Format: v1|enabled|host|port|mode|secretHex|fakeTlsDomain|label
        //   - enabled: '0' or '1'
        //   - host, fakeTlsDomain, label: pipe-and-backslash-escaped strings
        //   - port: int
        //   - mode: 0/1/2
        //   - secretHex: 32 hex chars
        private static ProxyConfig ParsePersisted(string raw)
        {
            try
            {
                const string prefix = "v1|";
                if (raw == null || !raw.StartsWith(prefix, StringComparison.Ordinal)) return null;
                string body = raw.Substring(prefix.Length);

                string[] parts = SplitWithEscape(body);
                if (parts == null || parts.Length < 7) return null;

                bool enabled = parts[0] == "1";
                string host = Unescape(parts[1]);
                int port;
                if (!int.TryParse(parts[2], System.Globalization.NumberStyles.Integer,
                                  System.Globalization.CultureInfo.InvariantCulture, out port))
                {
                    return null;
                }
                int modeI;
                if (!int.TryParse(parts[3], System.Globalization.NumberStyles.Integer,
                                  System.Globalization.CultureInfo.InvariantCulture, out modeI))
                {
                    return null;
                }
                if (modeI < 0 || modeI > 2) return null;

                byte[] secret = DecodeHex(parts[4]);
                string fakeSni = Unescape(parts[5]);
                string label = Unescape(parts[6]);

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
            catch
            {
                return null;
            }
        }

        private static string[] SplitWithEscape(string body)
        {
            var fields = new System.Collections.Generic.List<string>(8);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c == '\\' && i + 1 < body.Length)
                {
                    sb.Append(body[++i]);
                    continue;
                }
                if (c == '|')
                {
                    fields.Add(sb.ToString());
                    sb.Length = 0;
                    continue;
                }
                sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields.ToArray();
        }

        private static string Unescape(string s)
        {
            // SplitWithEscape already handled escaping during the split.
            // For a robust round-trip we return s as-is here; the
            // PreferenceSerializer's Escape only emits '\\' before '|'
            // or '\\' itself, both of which the splitter handled.
            return s ?? string.Empty;
        }

        private static byte[] DecodeHex(string hex)
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
    }
}
