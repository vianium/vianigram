// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading.Tasks;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Sync.Infrastructure
{
    /// <summary>
    /// STUB for <see cref="IUpdatesPort"/>.
    ///
    /// The MTProto WinMD currently exposes <c>MtProtoChannel.CallAsync</c> (RPC)
    /// but does not yet surface a push-channel subscription path. Until that
    /// lands, Sync runs without real-time updates: bootstrap +
    /// explicit getDifference is the only way new server state reaches the
    /// managed contexts.
    ///
    /// This stub satisfies the IUpdatesPort contract:
    /// <see cref="Subscribe"/> returns a no-op IDisposable. Composition wires it
    /// in by default; once the native push channel is exposed, a real adapter
    /// (e.g. <c>MtProtoUpdatesAdapter</c> in Composition) replaces this with
    /// no further changes to the Sync application layer.
    /// </summary>
    public sealed class StubUpdatesPort : IUpdatesPort
    {
        public IDisposable Subscribe(Func<byte[], Task> handler)
        {
            // Intentionally drop the handler. There is no producer side wired.
            // The returned token's Dispose is also a no-op so call sites that
            // wrap `using (var sub = port.Subscribe(...))` remain correct.
            return new NoopDisposable();
        }

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { /* no-op */ }
        }
    }
}
