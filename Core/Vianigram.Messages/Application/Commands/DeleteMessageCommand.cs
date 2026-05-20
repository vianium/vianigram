// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Application.Commands
{
    public sealed class DeleteMessageCommand
    {
        public DeleteMessageCommand(string peerKey, long messageId, bool forBoth)
        {
            PeerKey = peerKey;
            MessageId = messageId;
            ForBoth = forBoth;
        }

        public string PeerKey { get; private set; }
        public long MessageId { get; private set; }

        /// <summary>True maps to TL flag <c>revoke</c>: delete for everyone in the dialog.</summary>
        public bool ForBoth { get; private set; }
    }
}
