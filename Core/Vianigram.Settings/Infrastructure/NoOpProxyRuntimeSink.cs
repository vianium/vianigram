// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Stub <see cref="IProxyRuntimeSink"/> for unit tests, the in-memory
    /// composition root, and smoke-test scenarios where Vianigram.MTProto
    /// is not loaded. Discards every Apply call.
    /// </summary>
    public sealed class NoOpProxyRuntimeSink : IProxyRuntimeSink
    {
        public void Apply(ProxyConfig config)
        {
            // intentional no-op
        }
    }
}
