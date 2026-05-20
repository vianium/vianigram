// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PeerAccessHashAdapter.cs — Vianigram.Composition.Infrastructure
//
// Bridges the Messages context's IPeerAccessHashPort to the process-wide
// IPeerCache. The cache is hydrated by every typed RPC response that
// carries users:Vector<User> / chats:Vector<Chat> (see
// MtProtoChannelAdapter.HydratePeerCacheFromResponse), so by the time a
// chat-page tap reaches LoadHistoryHandler the (id, access_hash) pair is
// already in memory.

using MessagesPeerAccessHashPort = Vianigram.Messages.Ports.Outbound.IPeerAccessHashPort;
using NotificationsPeerAccessHashPort = Vianigram.Notifications.Ports.Outbound.IPeerAccessHashPort;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class PeerAccessHashAdapter : MessagesPeerAccessHashPort, NotificationsPeerAccessHashPort
    {
        private readonly IPeerCache _cache;

        public PeerAccessHashAdapter(IPeerCache cache)
        {
            _cache = cache;
        }

        public long GetUserAccessHash(long userId)
        {
            if (_cache == null) return 0L;
            long? v = _cache.GetUserAccessHash(userId);
            return v.HasValue ? v.Value : 0L;
        }

        public long GetChannelAccessHash(long channelId)
        {
            if (_cache == null) return 0L;
            long? v = _cache.GetChannelAccessHash(channelId);
            return v.HasValue ? v.Value : 0L;
        }
    }
}
