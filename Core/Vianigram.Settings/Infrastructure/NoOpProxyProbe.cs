// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Stub <see cref="IProxyProbe"/> that returns a synthetic
    /// "Reachable" result. Used by hosts that don't load Vianium.MtProxy
    /// (in-memory tests, smoke runners without the native lib).
    /// </summary>
    public sealed class NoOpProxyProbe : IProxyProbe
    {
        public Task<ProxyProbeResult> ProbeAsync(ProxyConfig config, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<ProxyProbeResult>();
            tcs.SetResult(ProxyProbeResult.Ok(0, "probe not wired"));
            return tcs.Task;
        }
    }
}
