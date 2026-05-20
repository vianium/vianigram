// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Ports.Outbound
{
    /// <summary>
    /// Outbound port for the "Test connection" button on the proxy
    /// settings page. Implementations perform a live obfuscated
    /// handshake against the configured proxy WITHOUT touching the
    /// active runtime state — the probe must never disturb the
    /// in-flight MTProto channel.
    ///
    /// The canonical implementation lives in
    /// <c>Vianigram.Composition.Infrastructure.MtProxyProbe</c> and
    /// uses the WinRT projection from <c>Vianium.MtProxy</c> to build
    /// the init packet against a fresh StreamSocket. A NoOp stub
    /// returns <see cref="ProxyProbeStatus.Reachable"/> for tests and
    /// degraded-mode hosts that don't load the native lib.
    /// </summary>
    public interface IProxyProbe
    {
        Task<ProxyProbeResult> ProbeAsync(ProxyConfig config, CancellationToken ct);
    }
}
