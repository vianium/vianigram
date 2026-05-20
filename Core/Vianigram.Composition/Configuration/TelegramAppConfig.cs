// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Composition.Configuration
{
    /// <summary>
    /// Shared Telegram application credentials and client metadata.
    /// <para>
    /// <b>Credentials policy.</b> <see cref="ApiId"/> and <see cref="ApiHash"/>
    /// identify the host application to Telegram. They are issued per-app at
    /// <see href="https://my.telegram.org"/> and Telegram's Terms of Service
    /// prohibit publishing them. The committed copy of this file therefore
    /// ships with placeholders. Real credentials live in
    /// <c>TelegramAppConfig.Local.cs</c> (gitignored) — see
    /// <c>TelegramAppConfig.Local.cs.example</c> for the pattern. Without a
    /// Local override, Telegram rejects the auth_key handshake; that is the
    /// expected behavior in a public fork until the consumer registers their
    /// own app and provides values here.
    /// </para>
    /// </summary>
    internal static partial class TelegramAppConfig
    {
        // Placeholders — overridden by TelegramAppConfig.Local.cs (gitignored).
        private static int _apiId = 0;
        private static string _apiHash = "REPLACE_WITH_YOUR_API_HASH";

        static TelegramAppConfig()
        {
            InitializeFromLocal();
        }

        /// <summary>
        /// Populated by <c>TelegramAppConfig.Local.cs</c>. The file is
        /// excluded from version control; consumers create their own.
        /// </summary>
        static partial void InitializeFromLocal();

        public static int ApiId { get { return _apiId; } }
        public static string ApiHash { get { return _apiHash; } }

        public const bool UseTestEnvironment = false;
        public const int ActiveDcId = 2;

        public const string AppTitle = "Vianigram";
        public const string DeviceModel = "Windows Phone";
        public const string SystemVersion = "Windows Phone 8.1";
        public const string AppVersion = "0.1.3.0";
        public const string SystemLangCode = "en";
        public const string LangPack = "";
        public const string LangCode = "en";
    }

    internal sealed class TelegramDcEndpoint
    {
        public TelegramDcEndpoint(
            int dcId,
            string host,
            int port,
            bool ipv6,
            bool mediaOnly,
            bool staticOption,
            bool thisPortOnly,
            int order)
        {
            DcId = dcId;
            Host = host;
            Port = port;
            Ipv6 = ipv6;
            MediaOnly = mediaOnly;
            StaticOption = staticOption;
            ThisPortOnly = thisPortOnly;
            Order = order;
        }

        public int DcId { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }
        public bool Ipv6 { get; private set; }
        public bool MediaOnly { get; private set; }
        public bool StaticOption { get; private set; }
        public bool ThisPortOnly { get; private set; }
        public int Order { get; private set; }

        public string Key
        {
            get { return BuildKey(Host, Port); }
        }

        public override string ToString()
        {
            return Host + ":" + Port;
        }

        internal static string BuildKey(string host, int port)
        {
            return (host ?? string.Empty) + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    internal static class TelegramDcOptions
    {
        public const int DefaultPort = 443;
        public static readonly TimeSpan CachedOpenTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan AuthKeyGenerationTimeout = TimeSpan.FromSeconds(16);
        public static readonly TimeSpan ChannelOpenTimeout = TimeSpan.FromSeconds(7);

        private static readonly object HealthGate = new object();
        private static readonly Dictionary<string, EndpointHealth> Health =
            new Dictionary<string, EndpointHealth>(StringComparer.OrdinalIgnoreCase);
        private static readonly PhoneDcRule[] LoginPhoneDcRules =
            new[]
            {
                // TDLib can ingest server-provided simple-config phone-prefix
                // rules. Until Vianigram has that fetch path, keep this seed
                // table narrow and let PHONE_MIGRATE_X correct it if Telegram
                // changes placement. Current live logs show +52 -> DC#1.
                new PhoneDcRule("52", 1)
            };

        public static bool TryGetEndpoint(int dcId, bool useTestEnvironment, out string host, out int port)
        {
            TelegramDcEndpoint[] endpoints = GetConnectionPlan(dcId, useTestEnvironment, null, 0);
            if (endpoints.Length == 0)
            {
                host = null;
                port = DefaultPort;
                return false;
            }

            host = endpoints[0].Host;
            port = endpoints[0].Port;
            return true;
        }

        public static int GuessLoginDcIdForPhone(string phoneE164)
        {
            string digits = NormalizePhoneDigits(phoneE164);
            if (digits.Length == 0)
            {
                return 0;
            }

            int bestDcId = 0;
            int bestPrefixLength = -1;
            for (int i = 0; i < LoginPhoneDcRules.Length; i++)
            {
                PhoneDcRule rule = LoginPhoneDcRules[i];
                if (rule == null || rule.DcId <= 0 || string.IsNullOrEmpty(rule.PrefixDigits))
                {
                    continue;
                }

                if (digits.StartsWith(rule.PrefixDigits, StringComparison.Ordinal) &&
                    rule.PrefixDigits.Length > bestPrefixLength)
                {
                    bestPrefixLength = rule.PrefixDigits.Length;
                    bestDcId = rule.DcId;
                }
            }

            return bestDcId;
        }

        public static TelegramDcEndpoint[] GetConnectionPlan(
            int dcId,
            bool useTestEnvironment,
            string preferredHost,
            int preferredPort)
        {
            List<TelegramDcEndpoint> endpoints = BuildBuiltInEndpoints(useTestEnvironment);
            List<TelegramDcEndpoint> selected = new List<TelegramDcEndpoint>();
            for (int i = 0; i < endpoints.Count; i++)
            {
                TelegramDcEndpoint endpoint = endpoints[i];
                if (endpoint.DcId == dcId && !endpoint.MediaOnly)
                {
                    selected.Add(endpoint);
                }
            }

            if (!string.IsNullOrEmpty(preferredHost) && preferredPort > 0)
            {
                bool found = false;
                bool knownOnAnotherDc = false;
                for (int i = 0; i < selected.Count; i++)
                {
                    TelegramDcEndpoint endpoint = selected[i];
                    if (string.Equals(endpoint.Host, preferredHost, StringComparison.OrdinalIgnoreCase) &&
                        endpoint.Port == preferredPort)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    for (int i = 0; i < endpoints.Count; i++)
                    {
                        TelegramDcEndpoint endpoint = endpoints[i];
                        if (endpoint.DcId != dcId &&
                            string.Equals(endpoint.Host, preferredHost, StringComparison.OrdinalIgnoreCase) &&
                            endpoint.Port == preferredPort)
                        {
                            knownOnAnotherDc = true;
                            break;
                        }
                    }
                }

                if (!found && !knownOnAnotherDc)
                {
                    selected.Add(new TelegramDcEndpoint(
                        dcId,
                        preferredHost,
                        preferredPort,
                        preferredHost.IndexOf(':') >= 0,
                        false,
                        false,
                        true,
                        -1));
                }
            }

            DateTime now = DateTime.UtcNow;
            selected.Sort(delegate(TelegramDcEndpoint a, TelegramDcEndpoint b)
            {
                int preferred = ComparePreferred(a, b, preferredHost, preferredPort);
                if (preferred != 0) return preferred;

                EndpointHealth ah = GetHealthSnapshot(a);
                EndpointHealth bh = GetHealthSnapshot(b);
                bool aCooling = ah != null && ah.CooldownUntilUtc > now;
                bool bCooling = bh != null && bh.CooldownUntilUtc > now;
                if (aCooling != bCooling) return aCooling ? 1 : -1;

                int aFailures = ah == null ? 0 : ah.Failures;
                int bFailures = bh == null ? 0 : bh.Failures;
                if (aFailures != bFailures) return aFailures.CompareTo(bFailures);

                return a.Order.CompareTo(b.Order);
            });

            return selected.ToArray();
        }

        public static void ReportEndpointSuccess(TelegramDcEndpoint endpoint)
        {
            if (endpoint == null) return;
            lock (HealthGate)
            {
                EndpointHealth health;
                if (!Health.TryGetValue(endpoint.Key, out health))
                {
                    return;
                }

                health.Failures = 0;
                health.CooldownUntilUtc = DateTime.MinValue;
                health.LastSuccessUtc = DateTime.UtcNow;
            }
        }

        public static void ReportEndpointFailure(TelegramDcEndpoint endpoint)
        {
            if (endpoint == null) return;
            lock (HealthGate)
            {
                EndpointHealth health;
                if (!Health.TryGetValue(endpoint.Key, out health))
                {
                    health = new EndpointHealth();
                    Health[endpoint.Key] = health;
                }

                health.Failures++;
                health.LastFailureUtc = DateTime.UtcNow;
                int cooldownSeconds = Math.Min(120, 10 * health.Failures);
                health.CooldownUntilUtc = health.LastFailureUtc.AddSeconds(cooldownSeconds);
            }
        }

        public static string DescribePlan(TelegramDcEndpoint[] endpoints)
        {
            if (endpoints == null || endpoints.Length == 0)
            {
                return "(empty)";
            }

            string[] parts = new string[endpoints.Length];
            for (int i = 0; i < endpoints.Length; i++)
            {
                parts[i] = endpoints[i].ToString();
            }
            return string.Join(",", parts);
        }

        private static int ComparePreferred(
            TelegramDcEndpoint a,
            TelegramDcEndpoint b,
            string preferredHost,
            int preferredPort)
        {
            if (string.IsNullOrEmpty(preferredHost) || preferredPort <= 0)
            {
                return 0;
            }

            bool ap = string.Equals(a.Host, preferredHost, StringComparison.OrdinalIgnoreCase) &&
                a.Port == preferredPort;
            bool bp = string.Equals(b.Host, preferredHost, StringComparison.OrdinalIgnoreCase) &&
                b.Port == preferredPort;
            if (ap == bp) return 0;
            return ap ? -1 : 1;
        }

        private static EndpointHealth GetHealthSnapshot(TelegramDcEndpoint endpoint)
        {
            lock (HealthGate)
            {
                EndpointHealth health;
                if (!Health.TryGetValue(endpoint.Key, out health))
                {
                    return null;
                }

                return new EndpointHealth
                {
                    Failures = health.Failures,
                    CooldownUntilUtc = health.CooldownUntilUtc,
                    LastFailureUtc = health.LastFailureUtc,
                    LastSuccessUtc = health.LastSuccessUtc
                };
            }
        }

        private static string NormalizePhoneDigits(string phoneE164)
        {
            if (string.IsNullOrWhiteSpace(phoneE164))
            {
                return string.Empty;
            }

            char[] buffer = new char[phoneE164.Length];
            int count = 0;
            for (int i = 0; i < phoneE164.Length; i++)
            {
                char c = phoneE164[i];
                if (c >= '0' && c <= '9')
                {
                    buffer[count++] = c;
                }
            }

            return count == 0 ? string.Empty : new string(buffer, 0, count);
        }

        private static List<TelegramDcEndpoint> BuildBuiltInEndpoints(bool useTestEnvironment)
        {
            List<TelegramDcEndpoint> endpoints = new List<TelegramDcEndpoint>();
            int order = 0;

            if (useTestEnvironment)
            {
                AddIpv4(endpoints, 1, new[] { "149.154.175.10", "149.154.175.40" }, ref order);
                AddIpv4(endpoints, 2, new[] { "149.154.167.40" }, ref order);
                AddIpv4(endpoints, 3, new[] { "149.154.175.117" }, ref order);
                AddIpv6(endpoints, 1, "2001:b28:f23d:f001::e", ref order);
                AddIpv6(endpoints, 2, "2001:67c:4e8:f002::e", ref order);
                AddIpv6(endpoints, 3, "2001:b28:f23d:f003::e", ref order);
                return endpoints;
            }

            AddIpv4(endpoints, 1, new[] { "149.154.175.50" }, ref order);
            AddIpv4(endpoints, 2, new[] { "149.154.167.51", "95.161.76.100", "149.154.167.50" }, ref order);
            AddIpv4(endpoints, 3, new[] { "149.154.175.100" }, ref order);
            AddIpv4(endpoints, 4, new[] { "149.154.167.91" }, ref order);
            AddIpv4(endpoints, 5, new[] { "149.154.171.5", "91.108.56.130" }, ref order);

            AddIpv6(endpoints, 1, "2001:b28:f23d:f001::a", ref order);
            AddIpv6(endpoints, 2, "2001:67c:4e8:f002::a", ref order);
            AddIpv6(endpoints, 3, "2001:b28:f23d:f003::a", ref order);
            AddIpv6(endpoints, 4, "2001:67c:4e8:f004::a", ref order);
            AddIpv6(endpoints, 5, "2001:b28:f23f:f005::a", ref order);
            return endpoints;
        }

        private static void AddIpv4(List<TelegramDcEndpoint> endpoints, int dcId, string[] hosts, ref int order)
        {
            int[] ports = new[] { 443, 80, 5222 };
            for (int p = 0; p < ports.Length; p++)
            {
                for (int h = 0; h < hosts.Length; h++)
                {
                    endpoints.Add(new TelegramDcEndpoint(
                        dcId,
                        hosts[h],
                        ports[p],
                        false,
                        false,
                        false,
                        ports[p] == 443,
                        order++));
                }
            }
        }

        private static void AddIpv6(List<TelegramDcEndpoint> endpoints, int dcId, string host, ref int order)
        {
            // Keep IPv6 behind IPv4. On WP 8.1 many mobile networks expose
            // IPv6 inconsistently, but having it in the plan helps dual-stack
            // networks without risking first-choice latency.
            endpoints.Add(new TelegramDcEndpoint(
                dcId,
                host,
                443,
                true,
                false,
                false,
                true,
                order++));
        }

        private sealed class EndpointHealth
        {
            public int Failures;
            public DateTime CooldownUntilUtc;
            public DateTime LastFailureUtc;
            public DateTime LastSuccessUtc;
        }

        private sealed class PhoneDcRule
        {
            public PhoneDcRule(string prefixDigits, int dcId)
            {
                PrefixDigits = prefixDigits ?? string.Empty;
                DcId = dcId;
            }

            public string PrefixDigits;
            public int DcId;
        }
    }
}
