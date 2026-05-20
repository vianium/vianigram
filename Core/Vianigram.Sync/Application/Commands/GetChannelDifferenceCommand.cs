// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Sync.Application.Commands
{
    /// <summary>
    /// Request updates.getChannelDifference for a specific channel — used when
    /// SyncState detected a per-channel pts gap (or when the user joins a channel
    /// for the first time and we need to establish a starting cursor).
    /// </summary>
    public sealed class GetChannelDifferenceCommand
    {
        public GetChannelDifferenceCommand(long channelId, long accessHash)
        {
            if (channelId <= 0) throw new ArgumentOutOfRangeException("channelId");
            ChannelId = channelId;
            AccessHash = accessHash;
        }

        public long ChannelId { get; private set; }
        public long AccessHash { get; private set; }
    }
}
