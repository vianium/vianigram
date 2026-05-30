// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Storage.Ports.Stubs;

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
        // ChannelOpen budgets — also ARM-aware. The "cached" path
        // (auth_key already in store) still does TCP connect + framing
        // greeting + init_connection RPC + first-frame decrypt. On
        // ARM Lumia hardware that round-trip + AES-IGE decrypt on the
        // initial msgs_container observed ~7-11 s wall time. 5 s was
        // cutting it close even on a healthy network. 15 s gives the
        // slow-CPU device room without making a dead endpoint
        // pathological — the intra-DC walk caps at 3 attempts so the
        // worst-case for cache-hit boot is ~45 s.
        public static readonly TimeSpan CachedOpenTimeout = TimeSpan.FromSeconds(15);
        // 16 s is enough for the DH handshake on a modern x86 desktop
        // (the emulator hits ~12 s reliably). ARM-on-WP8.1 hardware has
        // no SIMD acceleration for the 2048-bit RSA ModPow that
        // dominates step 3 / step 5; observed wall times on real
        // devices land in the 18-25 s range, hitting the timeout
        // before the handshake completes. Bump the cap so legitimate
        // slow-CPU handshakes can finish; the outer race wall
        // deadline (45 s) still bounds total bootstrap time.
        public static readonly TimeSpan AuthKeyGenerationTimeout = TimeSpan.FromSeconds(35);
        public static readonly TimeSpan ChannelOpenTimeout = TimeSpan.FromSeconds(20);

        private static readonly object HealthGate = new object();
        private static readonly Dictionary<string, EndpointHealth> Health =
            new Dictionary<string, EndpointHealth>(StringComparer.OrdinalIgnoreCase);

        // Persistent store wired by the composition root. When null we
        // operate in process-only mode (legacy behavior). When set, every
        // success/failure is fire-and-forget upserted so the next cold
        // start sees the same view of which endpoints are dead.
        private static IEndpointHealthStore _healthStore;

        // Persistent dc_options snapshot. Populated by
        // <see cref="AttachPersistentDcOptionsStoreAsync"/> from the
        // server's <c>help.getConfig</c> response, persisted across
        // launches. Augments (does not replace) the hardcoded bootstrap
        // plan. Keyed by (dcId, host, port, ipv6) under the assumption
        // that the server returns canonical IPs.
        private static readonly object DcOptionsGate = new object();
        private static List<DcOptionRecord> _persistedDcOptions = new List<DcOptionRecord>();
        private static IDcOptionsStore _dcOptionsStore;

        // Maximum cooldown when an endpoint has crossed the
        // "consistently broken across launches" threshold (10+ persistent
        // failures, zero successes since last hydrate). The cap is 1 hour
        // — longer than the typical mobile-network handover window so
        // users on a recovering network still get re-probed reasonably
        // soon, but long enough that a known-dead `:5222` (we removed it
        // from the plan but the same logic applies to any future bad
        // host) doesn't waste a single keygen round-trip.
        private const int PersistentlyBrokenCooldownSeconds = 3600;
        private const int PersistentlyBrokenFailureThreshold = 10;
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

                // 1. Cooling endpoints last (recently failed, waiting out
                //    the cooldown).
                bool aCooling = ah != null && ah.CooldownUntilUtc > now;
                bool bCooling = bh != null && bh.CooldownUntilUtc > now;
                if (aCooling != bCooling) return aCooling ? 1 : -1;

                // 2. Endpoints with a recorded success first; among those,
                //    most recent success wins (TDLib DcOptionsSet.cpp:150
                //    "Ok > Connecting > Error" maps to this).
                bool aSuccess = ah != null && ah.LastSuccessUtc != DateTime.MinValue;
                bool bSuccess = bh != null && bh.LastSuccessUtc != DateTime.MinValue;
                if (aSuccess != bSuccess) return aSuccess ? -1 : 1;
                if (aSuccess && bSuccess)
                {
                    int success = bh.LastSuccessUtc.CompareTo(ah.LastSuccessUtc);
                    if (success != 0) return success;
                }

                // 3. Among neither-success endpoints, fewer recorded
                //    failures first — an endpoint that has timed out 5×
                //    is statistically worse than one that hasn't been
                //    tried yet (Failures == 0 with LastSuccessUtc ==
                //    MinValue means "fresh, never tried").
                int af = ah != null ? ah.Failures : 0;
                int bf = bh != null ? bh.Failures : 0;
                if (af != bf) return af.CompareTo(bf);

                // 4. Among same-failure-count, prefer the endpoint whose
                //    last failure was longer ago (more time to recover).
                if (ah != null && bh != null && ah.LastFailureUtc != bh.LastFailureUtc)
                {
                    return ah.LastFailureUtc.CompareTo(bh.LastFailureUtc);
                }

                return a.Order.CompareTo(b.Order);
            });

            return selected.ToArray();
        }

        public static void ReportEndpointSuccess(TelegramDcEndpoint endpoint)
        {
            if (endpoint == null) return;
            EndpointHealth snapshot;
            lock (HealthGate)
            {
                EndpointHealth health;
                if (!Health.TryGetValue(endpoint.Key, out health))
                {
                    health = new EndpointHealth();
                    Health[endpoint.Key] = health;
                }

                health.DcId = endpoint.DcId;
                health.Family = endpoint.Ipv6 ? 6 : 4;
                health.Failures = 0;
                health.Successes++;
                health.CooldownUntilUtc = DateTime.MinValue;
                health.LastSuccessUtc = DateTime.UtcNow;
                health.LastFailureReason = string.Empty;
                snapshot = CloneLocked(health);
            }

            FireAndForgetPersist(endpoint, snapshot);
        }

        public static void ReportEndpointFailure(TelegramDcEndpoint endpoint)
        {
            ReportEndpointFailure(endpoint, null);
        }

        /// <summary>
        /// Overload that also records the reason text (e.g.
        /// <c>"WSAEHOSTUNREACH"</c>). Reasons survive across launches via
        /// the persistent store so we can decide e.g. "this host has been
        /// HOST_UNREACHABLE 12 times — keep it deprioritised for an
        /// hour even if a healthy endpoint exists in the same plan".
        /// </summary>
        public static void ReportEndpointFailure(TelegramDcEndpoint endpoint, string reason)
        {
            if (endpoint == null) return;
            EndpointHealth snapshot;
            lock (HealthGate)
            {
                EndpointHealth health;
                if (!Health.TryGetValue(endpoint.Key, out health))
                {
                    health = new EndpointHealth();
                    Health[endpoint.Key] = health;
                }

                health.DcId = endpoint.DcId;
                health.Family = endpoint.Ipv6 ? 6 : 4;
                health.Failures++;
                health.LastFailureUtc = DateTime.UtcNow;
                health.LastFailureReason = reason ?? health.LastFailureReason ?? string.Empty;

                int cooldownSeconds;
                if (health.Failures >= PersistentlyBrokenFailureThreshold && health.Successes == 0)
                {
                    // No proof this endpoint EVER worked from this device;
                    // sit it out for an hour. Networks heal slowly; users
                    // on a flaky mobile carrier benefit from a long
                    // backoff over churning the radio.
                    cooldownSeconds = PersistentlyBrokenCooldownSeconds;
                }
                else
                {
                    // Short, linearly escalating backoff while we still
                    // have hope (or proof) the endpoint can recover.
                    cooldownSeconds = Math.Min(120, 10 * health.Failures);
                }
                health.CooldownUntilUtc = health.LastFailureUtc.AddSeconds(cooldownSeconds);
                snapshot = CloneLocked(health);
            }

            FireAndForgetPersist(endpoint, snapshot);
        }

        /// <summary>
        /// Wires a persistent store and hydrates the in-memory dictionary
        /// from it. Idempotent: a second call replaces the store but
        /// preserves the in-memory snapshot built so far (so we don't
        /// lose mid-session signal if composition decides to swap stores).
        /// Call this once at composition, after SQLite is ready and
        /// before TelegramDcOptions.GetConnectionPlan is hit by any
        /// auth_key path.
        /// </summary>
        public static async Task AttachPersistentHealthStoreAsync(
            IEndpointHealthStore store,
            CancellationToken ct)
        {
            if (store == null) return;

            // Prune very old entries first — a stale 1-year-old cooldown
            // for an endpoint Telegram has since rebalanced would just
            // delay our first real probe.
            try
            {
                await store.PruneOlderThanAsync(
                    DateTime.UtcNow.AddDays(-7), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "TelegramDcOptions",
                    "persistent health prune threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            List<EndpointHealthRecord> rows;
            try
            {
                rows = await store.LoadAllAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "TelegramDcOptions",
                    "persistent health load threw: " + ex.GetType().Name + ": " + ex.Message);
                rows = null;
            }

            lock (HealthGate)
            {
                _healthStore = store;
                if (rows != null)
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        EndpointHealthRecord r = rows[i];
                        if (r == null) continue;

                        string key = TelegramDcEndpoint.BuildKey(r.Host, r.Port);
                        EndpointHealth h = new EndpointHealth
                        {
                            DcId = r.DcId,
                            Family = r.Family,
                            Failures = r.Failures,
                            Successes = r.Successes,
                            LastFailureUtc = r.LastFailureUtc,
                            LastSuccessUtc = r.LastSuccessUtc,
                            CooldownUntilUtc = r.CooldownUntilUtc,
                            LastFailureReason = r.LastFailureReason ?? string.Empty
                        };
                        Health[key] = h;
                    }
                }
            }

            EarlyLog.Write(
                "TelegramDcOptions",
                "persistent health attached: hydrated=" +
                (rows != null ? rows.Count : 0));
        }

        /// <summary>
        /// Wires a persistent dc_options store and hydrates the in-memory
        /// snapshot from it. Merging happens at plan-build time
        /// (<see cref="BuildBuiltInEndpoints"/>), not at attach time, so
        /// even if attach hasn't completed yet the next plan will pick
        /// up whatever was successfully loaded.
        /// </summary>
        public static async Task AttachPersistentDcOptionsStoreAsync(
            IDcOptionsStore store,
            CancellationToken ct)
        {
            if (store == null) return;

            List<DcOptionRecord> rows;
            try
            {
                rows = await store.LoadAllAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "TelegramDcOptions",
                    "persistent dc_options load threw: " +
                    ex.GetType().Name + ": " + ex.Message);
                rows = null;
            }

            lock (DcOptionsGate)
            {
                _dcOptionsStore = store;
                if (rows != null)
                {
                    _persistedDcOptions = rows;
                }
            }

            EarlyLog.Write(
                "TelegramDcOptions",
                "persistent dc_options attached: hydrated=" +
                (rows != null ? rows.Count : 0));
        }

        /// <summary>
        /// Called from the auth_key path after a successful
        /// <c>help.getConfig</c>. Replaces both the in-memory snapshot
        /// AND the persisted set in a single transaction. Idempotent.
        /// </summary>
        public static async Task ReplaceDcOptionsAsync(
            IReadOnlyList<DcOptionRecord> records,
            CancellationToken ct)
        {
            if (records == null) return;

            IDcOptionsStore store;
            lock (DcOptionsGate)
            {
                _persistedDcOptions = new List<DcOptionRecord>(records);
                store = _dcOptionsStore;
            }

            if (store != null)
            {
                try
                {
                    await store.ReplaceAllAsync(records, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    EarlyLog.Write(
                        "TelegramDcOptions",
                        "persistent dc_options replace threw: " +
                        ex.GetType().Name + ": " + ex.Message);
                }
            }

            EarlyLog.Write(
                "TelegramDcOptions",
                "dc_options refreshed in-process count=" + records.Count);
        }

        /// <summary>
        /// Snapshot used by <see cref="BuildBuiltInEndpoints"/> to merge
        /// persisted IPs over the hardcoded plan. Returns a per-DC bucket
        /// for the requested DC; empty list when nothing is persisted.
        /// </summary>
        private static List<DcOptionRecord> GetPersistedFor(int dcId)
        {
            lock (DcOptionsGate)
            {
                if (_persistedDcOptions == null || _persistedDcOptions.Count == 0)
                {
                    return new List<DcOptionRecord>();
                }
                List<DcOptionRecord> result = new List<DcOptionRecord>();
                for (int i = 0; i < _persistedDcOptions.Count; i++)
                {
                    DcOptionRecord r = _persistedDcOptions[i];
                    if (r != null && r.DcId == dcId)
                    {
                        result.Add(r);
                    }
                }
                return result;
            }
        }

        private static EndpointHealth CloneLocked(EndpointHealth h)
        {
            return new EndpointHealth
            {
                DcId = h.DcId,
                Family = h.Family,
                Failures = h.Failures,
                Successes = h.Successes,
                LastFailureUtc = h.LastFailureUtc,
                LastSuccessUtc = h.LastSuccessUtc,
                CooldownUntilUtc = h.CooldownUntilUtc,
                LastFailureReason = h.LastFailureReason ?? string.Empty
            };
        }

        private static void FireAndForgetPersist(TelegramDcEndpoint endpoint, EndpointHealth snapshot)
        {
            IEndpointHealthStore store = _healthStore;
            if (store == null || endpoint == null || snapshot == null) return;

            int family = snapshot.Family != 0 ? snapshot.Family : (endpoint.Ipv6 ? 6 : 4);
            int dcId = snapshot.DcId != 0 ? snapshot.DcId : endpoint.DcId;

            EndpointHealthRecord record = new EndpointHealthRecord(
                endpoint.Host,
                endpoint.Port,
                dcId,
                family,
                snapshot.Failures,
                snapshot.Successes,
                snapshot.LastFailureUtc,
                snapshot.LastSuccessUtc,
                snapshot.CooldownUntilUtc,
                snapshot.LastFailureReason);

            // Fire-and-forget — the caller (auth_key race) cannot wait on
            // disk I/O. UpsertAsync uses SqliteDatabase.Gate internally
            // so concurrent calls from different endpoints are serialised
            // there. Exceptions are swallowed (logged) to keep the
            // critical path unaffected if the disk is full or the DB is
            // locked.
            Task.Run(async delegate
            {
                try
                {
                    await store.UpsertAsync(record, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    EarlyLog.Write(
                        "TelegramDcOptions",
                        "persistent health upsert threw for " + endpoint.ToString() +
                        ": " + ex.GetType().Name + ": " + ex.Message);
                }
            });
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

                return CloneLocked(health);
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

            AddIpv4(endpoints, 1, new[] { "149.154.175.50", "149.154.175.60", "149.154.175.55" }, ref order);
            AddIpv4(endpoints, 2, new[] { "149.154.167.51", "95.161.76.100", "149.154.167.50", "149.154.167.41" }, ref order);
            AddIpv4(endpoints, 3, new[] { "149.154.175.100" }, ref order);
            AddIpv4(endpoints, 4, new[] { "149.154.167.91" }, ref order);
            AddIpv4(endpoints, 5, new[] { "149.154.171.5", "91.108.56.130" }, ref order);

            AddIpv6(endpoints, 1, "2001:b28:f23d:f001::a", ref order);
            AddIpv6(endpoints, 2, "2001:67c:4e8:f002::a", ref order);
            AddIpv6(endpoints, 3, "2001:b28:f23d:f003::a", ref order);
            AddIpv6(endpoints, 4, "2001:67c:4e8:f004::a", ref order);
            AddIpv6(endpoints, 5, "2001:b28:f23f:f005::a", ref order);

            // Merge persisted dc_options from the most recent
            // help.getConfig response. This is what gives DC#1 (and any
            // other under-served DC in the hardcoded list) the extra IPs
            // it needs when the canonical one is unreachable from the
            // user's network. CDN-only and media-only options are
            // intentionally skipped — those serve content, not auth.
            for (int dcId = 1; dcId <= 5; dcId++)
            {
                List<DcOptionRecord> persisted = GetPersistedFor(dcId);
                for (int i = 0; i < persisted.Count; i++)
                {
                    DcOptionRecord r = persisted[i];
                    if (r == null) continue;
                    if (r.MediaOnly || r.Cdn || r.TcpoOnly) continue;

                    // Skip duplicates already in the hardcoded plan.
                    bool duplicate = false;
                    for (int j = 0; j < endpoints.Count; j++)
                    {
                        TelegramDcEndpoint existing = endpoints[j];
                        if (existing.DcId == r.DcId &&
                            existing.Port == r.Port &&
                            string.Equals(existing.Host, r.Host, StringComparison.OrdinalIgnoreCase))
                        {
                            duplicate = true;
                            break;
                        }
                    }
                    if (duplicate) continue;

                    endpoints.Add(new TelegramDcEndpoint(
                        r.DcId,
                        r.Host,
                        r.Port,
                        r.Ipv6,
                        r.MediaOnly,
                        r.StaticFlag,
                        r.ThisPortOnly,
                        order++));
                }
            }
            return endpoints;
        }

        private static void AddIpv4(List<TelegramDcEndpoint> endpoints, int dcId, string[] hosts, ref int order)
        {
            // Port catalogue rationale:
            //   443  — canonical MTProto-TCP (TLS-wrapped on the wire).
            //   80   — legitimate fallback for networks that strip 443
            //          plaintext-handshake bytes; documented in Telegram's
            //          public MTProto transport notes.
            //   5222 — REMOVED. That port is IANA-assigned to XMPP and was
            //          never an official MTProto endpoint. Live logs from
            //          the WP 8.1 emulator on a typical mobile/ISP network
            //          show every 5222 candidate timing out (16 s each)
            //          which dragged the staggered auth_key race up to
            //          ~24 s before any 443 candidate could win. Removing
            //          it caps the worst-case race wall time without
            //          changing the success path. If a future device
            //          actually needs a non-443/80 transport, prefer the
            //          MTProto websocket transport over hard-coding 5222.
            int[] ports = new[] { 443, 80 };
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
            public int DcId;
            public int Family;              // 4 or 6
            public int Failures;
            public int Successes;
            public DateTime CooldownUntilUtc;
            public DateTime LastFailureUtc;
            public DateTime LastSuccessUtc;
            public string LastFailureReason;
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
