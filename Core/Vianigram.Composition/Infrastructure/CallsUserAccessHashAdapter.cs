// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CallsUserAccessHashAdapter.cs — Vianigram.Composition.Infrastructure
//
// Bridges Vianigram.Calls' IUserAccessHashPort to the shared IPeerCache.
// Same pattern as PeerAccessHashAdapter (Messages context); the Calls
// context only needs user-id lookups (phone.requestCall takes inputUser),
// not channel-id lookups, so the surface is narrower.

using Vianigram.Calls.Ports.Outbound;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class CallsUserAccessHashAdapter : IUserAccessHashPort
    {
        private readonly IPeerCache _cache;

        public CallsUserAccessHashAdapter(IPeerCache cache)
        {
            _cache = cache;
        }

        public long GetUserAccessHash(long userId)
        {
            if (_cache == null) return 0L;
            long? v = _cache.GetUserAccessHash(userId);
            return v.HasValue ? v.Value : 0L;
        }
    }
}
