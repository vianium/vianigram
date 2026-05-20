// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Infrastructure
{
    /// <summary>
    /// Allocates negative client-temp ids for optimistic sends. Starts at -1
    /// and decrements monotonically; never collides with server-assigned
    /// positive ids, and resets per app launch (not persisted across boots —
    /// pending sends from previous sessions are reconciled by the outbox).
    /// </summary>
    public sealed class MonotonicMessageIdGenerator : IMessageIdGenerator
    {
        private long _next; // starts at 0; first call returns -1

        public long NextClientTempId()
        {
            long v = Interlocked.Decrement(ref _next);
            return v;
        }
    }
}
