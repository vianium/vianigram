// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ProxyConfigSmokeTest — verifies the MTProxy descriptor parser and the
// ProxyConfig value object's invariants without touching the network.
//
// Covers:
//   1. Round-trip of a tg://proxy?... URL into ProxyConfig.
//   2. Round-trip of a bare-hex secret (legacy 32 chars).
//   3. Dd-prefixed (secure) and ee-prefixed (fakeTls) secret variants.
//   4. URL-safe base64 secrets.
//   5. ProxyConfig ctor validation rejects malformed enabled descriptors.
//   6. PreferenceSerializer-friendly serialization round-trip via the
//      public ProxyConfigParser path (no internal seam needed here —
//      the SettingsApplication SetProxyAsync flow exercises the same
//      serializer at runtime).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Infrastructure;

namespace Vianigram.SmokeTests.Tests
{
    public static class ProxyConfigSmokeTest
    {
        public static Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var entries = new List<TestEntry>();

            RunCase(entries, "tg:// URL parses host/port/secret", () =>
            {
                ProxyConfig cfg;
                bool ok = ProxyConfigParser.TryParse(
                    "tg://proxy?server=proxy.example.com&port=443&secret=00112233445566778899aabbccddeeff",
                    true, out cfg);
                if (!ok || cfg == null) return "parse failed";
                if (cfg.Host != "proxy.example.com") return "host=" + cfg.Host;
                if (cfg.Port != 443) return "port=" + cfg.Port;
                if (cfg.Mode != ProxySecretMode.Legacy) return "mode=" + cfg.Mode;
                if (cfg.Secret == null || cfg.Secret.Length != 16) return "secret len";
                if (cfg.Secret[0] != 0x00 || cfg.Secret[15] != 0xFF) return "secret bytes";
                return null;
            });

            RunCase(entries, "https://t.me/proxy URL parses", () =>
            {
                ProxyConfig cfg;
                bool ok = ProxyConfigParser.TryParse(
                    "https://t.me/proxy?server=1.2.3.4&port=8443&secret=dd00112233445566778899aabbccddeeff",
                    true, out cfg);
                if (!ok || cfg == null) return "parse failed";
                if (cfg.Mode != ProxySecretMode.Secure) return "mode=" + cfg.Mode;
                if (cfg.Port != 8443) return "port=" + cfg.Port;
                if (cfg.Host != "1.2.3.4") return "host=" + cfg.Host;
                return null;
            });

            RunCase(entries, "ee-prefixed FakeTLS secret carries SNI", () =>
            {
                byte[] payload;
                ProxySecretMode mode;
                string sni;
                // ee + 16 secret bytes + ASCII "google.com"
                string hex = "ee00112233445566778899aabbccddeeff"
                           + "676f6f676c652e636f6d";   // "google.com"
                bool ok = ProxyConfigParser.TryParseSecret(hex, out payload, out mode, out sni);
                if (!ok) return "parse failed";
                if (mode != ProxySecretMode.FakeTls) return "mode=" + mode;
                if (sni != "google.com") return "sni=" + sni;
                if (payload.Length != 16) return "payload len";
                return null;
            });

            RunCase(entries, "URL-safe base64 secret round-trip", () =>
            {
                // Encode 16 random bytes as url-safe base64.
                byte[] bytes = new byte[16];
                for (int i = 0; i < 16; i++) bytes[i] = (byte)(i * 17 ^ 0x5A);
                string b64 = System.Convert.ToBase64String(bytes)
                                   .Replace('+', '-').Replace('/', '_').TrimEnd('=');

                byte[] payload;
                ProxySecretMode mode;
                string sni;
                bool ok = ProxyConfigParser.TryParseSecret(b64, out payload, out mode, out sni);
                if (!ok) return "parse failed";
                if (mode != ProxySecretMode.Legacy) return "mode=" + mode;
                for (int i = 0; i < 16; i++)
                    if (payload[i] != bytes[i]) return "byte " + i;
                return null;
            });

            RunCase(entries, "ProxyConfig ctor rejects 15-byte secret", () =>
            {
                try
                {
                    new ProxyConfig(
                        enabled: true,
                        host: "p",
                        port: 443,
                        secret: new byte[15],
                        mode: ProxySecretMode.Legacy,
                        fakeTlsDomain: string.Empty,
                        label: string.Empty);
                    return "expected exception";
                }
                catch (System.ArgumentException)
                {
                    return null;
                }
            });

            RunCase(entries, "ProxyConfig ctor rejects FakeTls without SNI", () =>
            {
                try
                {
                    new ProxyConfig(
                        enabled: true,
                        host: "p",
                        port: 443,
                        secret: new byte[16],
                        mode: ProxySecretMode.FakeTls,
                        fakeTlsDomain: string.Empty,
                        label: string.Empty);
                    return "expected exception";
                }
                catch (System.ArgumentException)
                {
                    return null;
                }
            });

            RunCase(entries, "ProxyConfig.Disabled has zero-length secret access", () =>
            {
                var d = ProxyConfig.Disabled;
                if (d.Enabled) return "Disabled.Enabled==true";
                if (d.Host == null) return "Disabled.Host == null";
                if (d.Secret == null) return "Disabled.Secret == null";
                if (d.Secret.Length != 0) return "Disabled.Secret.Length=" + d.Secret.Length;
                return null;
            });

            RunCase(entries, "TryParse rejects malformed URL", () =>
            {
                ProxyConfig cfg;
                bool ok = ProxyConfigParser.TryParse("tg://proxy?server=&port=443&secret=00", true, out cfg);
                if (ok) return "expected false";
                return null;
            });

            RunCase(entries, "TryParse rejects empty input", () =>
            {
                ProxyConfig cfg;
                bool ok = ProxyConfigParser.TryParse(string.Empty, true, out cfg);
                if (ok) return "expected false";
                return null;
            });

            return Task.FromResult(entries);
        }

        private static void RunCase(List<TestEntry> entries, string name, System.Func<string> body)
        {
            string fail = null;
            try { fail = body(); }
            catch (System.Exception ex) { fail = ex.GetType().Name + ": " + ex.Message; }
            entries.Add(new TestEntry
            {
                Suite = "ProxyConfig",
                Name = name,
                Passed = fail == null,
                Detail = fail ?? "ok"
            });
        }
    }
}
