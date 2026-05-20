// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Application.Commands
{
    public sealed class EditTextMessageCommand
    {
        public EditTextMessageCommand(string peerKey, long messageId, string newText)
        {
            PeerKey = peerKey;
            MessageId = messageId;
            NewText = newText;
        }

        public string PeerKey { get; private set; }
        public long MessageId { get; private set; }
        public string NewText { get; private set; }
    }
}
