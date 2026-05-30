// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// Per-endpoint reachability statistics. Persisted by
    /// <see cref="IEndpointHealthStore"/> so the staggered auth_key race
    /// can deprioritise endpoints that proved unreachable on prior
    /// launches instead of re-discovering the same failures every
    /// cold start.
    ///
    /// All numeric times are Unix ms UTC (<see cref="DateTime.MinValue"/>
    /// encoded as 0). All counts are non-negative.
    /// </summary>
    public sealed class EndpointHealthRecord
    {
        public string Host { get; private set; }
        public int Port { get; private set; }
        public int DcId { get; private set; }
        public int Family { get; private set; }      // 4 or 6
        public int Failures { get; private set; }
        public int Successes { get; private set; }
        public DateTime LastFailureUtc { get; private set; }
        public DateTime LastSuccessUtc { get; private set; }
        public DateTime CooldownUntilUtc { get; private set; }
        public string LastFailureReason { get; private set; }

        public EndpointHealthRecord(
            string host,
            int port,
            int dcId,
            int family,
            int failures,
            int successes,
            DateTime lastFailureUtc,
            DateTime lastSuccessUtc,
            DateTime cooldownUntilUtc,
            string lastFailureReason)
        {
            if (string.IsNullOrEmpty(host)) throw new ArgumentNullException("host");
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
            if (family != 4 && family != 6) throw new ArgumentOutOfRangeException("family");

            Host = host;
            Port = port;
            DcId = dcId;
            Family = family;
            Failures = failures < 0 ? 0 : failures;
            Successes = successes < 0 ? 0 : successes;
            LastFailureUtc = lastFailureUtc;
            LastSuccessUtc = lastSuccessUtc;
            CooldownUntilUtc = cooldownUntilUtc;
            LastFailureReason = lastFailureReason ?? string.Empty;
        }
    }
}
