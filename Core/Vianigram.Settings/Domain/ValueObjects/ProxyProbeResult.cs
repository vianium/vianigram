// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Outcome of a live MTProxy handshake probe initiated by the user
    /// from the proxy settings page. Carries a coarse-grained
    /// <see cref="Status"/> plus optional human-readable detail for
    /// surfacing in the UI.
    ///
    /// The probe is best-effort by design — getting bytes back from the
    /// proxy after writing the 64-byte init packet does NOT prove the
    /// secret is correct (that is verified by Telegram's own DH
    /// handshake on the next channel reopen), but a successful probe
    /// rules out host typos, blocked ports, and dead proxies.
    /// </summary>
    public sealed class ProxyProbeResult
    {
        public ProxyProbeStatus Status   { get; private set; }
        public long             ElapsedMs { get; private set; }
        public string           Detail   { get; private set; }

        public ProxyProbeResult(ProxyProbeStatus status, long elapsedMs, string detail)
        {
            Status = status;
            ElapsedMs = elapsedMs;
            Detail = detail ?? string.Empty;
        }

        public static ProxyProbeResult Ok(long elapsedMs, string detail)
        {
            return new ProxyProbeResult(ProxyProbeStatus.Reachable, elapsedMs, detail ?? "ok");
        }

        public static ProxyProbeResult Timeout(long elapsedMs)
        {
            return new ProxyProbeResult(ProxyProbeStatus.Timeout, elapsedMs, "no response within timeout");
        }

        public static ProxyProbeResult Rejected(long elapsedMs, string detail)
        {
            return new ProxyProbeResult(ProxyProbeStatus.Rejected, elapsedMs, detail ?? "proxy rejected the handshake");
        }

        public static ProxyProbeResult NetworkError(long elapsedMs, string detail)
        {
            return new ProxyProbeResult(ProxyProbeStatus.NetworkError, elapsedMs, detail ?? "network error");
        }

        public static ProxyProbeResult Misconfigured(string detail)
        {
            return new ProxyProbeResult(ProxyProbeStatus.Misconfigured, 0, detail ?? "invalid configuration");
        }
    }

    public enum ProxyProbeStatus
    {
        Reachable     = 0,   // socket opened, init packet sent, server returned bytes
        Timeout       = 1,   // socket opened, no bytes within deadline
        Rejected      = 2,   // socket opened, server closed without sending bytes
        NetworkError  = 3,   // socket open failed / RST / DNS failure
        Misconfigured = 4    // local validation failed (bad secret, etc.) — never reached the wire
    }
}
