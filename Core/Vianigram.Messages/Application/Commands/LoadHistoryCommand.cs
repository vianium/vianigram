// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Application.Commands
{
    public sealed class LoadHistoryCommand
    {
        public LoadHistoryCommand(string peerKey, long? offsetMsgId, int limit)
        {
            PeerKey = peerKey;
            OffsetMsgId = offsetMsgId;
            Limit = limit;
        }

        public string PeerKey { get; private set; }
        public long? OffsetMsgId { get; private set; }
        public int Limit { get; private set; }
    }
}
