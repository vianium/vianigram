// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Application.Queries
{
    public sealed class GetMessageQuery
    {
        public GetMessageQuery(string peerKey, long messageId)
        {
            PeerKey = peerKey;
            MessageId = messageId;
        }

        public string PeerKey { get; private set; }
        public long MessageId { get; private set; }
    }
}
