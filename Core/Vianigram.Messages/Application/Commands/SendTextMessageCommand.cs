// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Application.Commands
{
    public sealed class SendTextMessageCommand
    {
        public SendTextMessageCommand(string peerKey, string text, long? replyTo = null)
        {
            PeerKey = peerKey;
            Text = text;
            ReplyTo = replyTo;
        }

        public string PeerKey { get; private set; }
        public string Text { get; private set; }
        public long? ReplyTo { get; private set; }
    }
}
