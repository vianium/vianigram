// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Application.Commands
{
    public sealed class MarkAsReadCommand
    {
        public MarkAsReadCommand(string peerKey, long upToMessageId)
        {
            PeerKey = peerKey;
            UpToMessageId = upToMessageId;
        }

        public string PeerKey { get; private set; }
        public long UpToMessageId { get; private set; }
    }
}
