// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Messages.Ports.Inbound
{
    /// <summary>
    /// Lightweight cross-context notification raised when the message set
    /// for a peer changes (insert / edit / delete / read-cursor advance) —
    /// or when peer-level state visible in the chat surface (typing
    /// indicator, online presence) shifts.
    ///
    /// UI ViewModels subscribe via <see cref="IMessagesApi.MessagesChanged"/>
    /// and switch on <see cref="Kind"/>. Optional payload fields
    /// (<see cref="Body"/>, <see cref="FromUserId"/>, <see cref="LastOnlineUtc"/>,
    /// <see cref="TypingAction"/>, etc.) are populated only for the kinds
    /// that produce them; subscribers should treat them as nullable.
    /// </summary>
    public sealed class MessagesChangedEventArgs : EventArgs
    {
        // Backward-compat ctor — used by the existing legacy bus bridge
        // (`MessagesApplication.OnReceived(...)` etc.) for the thin
        // updates path that only carries peer + id.
        public MessagesChangedEventArgs(string peerKey, MessagesChangeKind kind, long? messageId = null, long? clientTempId = null)
            : this(peerKey, kind, messageId, clientTempId,
                   body: null, fromUserId: null,
                   isOnline: null, lastOnlineUtc: null,
                   typingAction: null)
        {
        }

        /// <summary>
        /// Richer ctor used by the Sync→Messages bridge so the UI can render
        /// the right state without re-fetching the conversation. All payload
        /// fields are optional; null marks "not applicable for this kind".
        /// </summary>
        public MessagesChangedEventArgs(
            string peerKey,
            MessagesChangeKind kind,
            long? messageId,
            long? clientTempId,
            string body,
            long? fromUserId,
            bool? isOnline,
            DateTime? lastOnlineUtc,
            string typingAction)
        {
            PeerKey = peerKey;
            Kind = kind;
            MessageId = messageId;
            ClientTempId = clientTempId;
            Body = body;
            FromUserId = fromUserId;
            IsOnline = isOnline;
            LastOnlineUtc = lastOnlineUtc;
            TypingAction = typingAction;
        }

        public string PeerKey { get; private set; }
        public MessagesChangeKind Kind { get; private set; }
        public long? MessageId { get; private set; }
        public long? ClientTempId { get; private set; }

        // ----- Payload fields ----------------------------------------------
        // All optional; consumers check Kind first then read the matching
        // field. null indicates "not surfaced for this Kind".

        /// <summary>Message body for <see cref="MessagesChangeKind.Received"/>.</summary>
        public string Body { get; private set; }

        /// <summary>Sender user id for group/channel <see cref="MessagesChangeKind.Received"/>.</summary>
        public long? FromUserId { get; private set; }

        /// <summary>Online flag for <see cref="MessagesChangeKind.PeerStatusChanged"/>.</summary>
        public bool? IsOnline { get; private set; }

        /// <summary>Last-online UTC for <see cref="MessagesChangeKind.PeerStatusChanged"/>.</summary>
        public DateTime? LastOnlineUtc { get; private set; }

        /// <summary>Action label for <see cref="MessagesChangeKind.PeerTypingChanged"/>.</summary>
        public string TypingAction { get; private set; }
    }

    public enum MessagesChangeKind
    {
        Queued = 0,
        Sent = 1,
        SendFailed = 2,
        Received = 3,
        Edited = 4,
        Deleted = 5,
        ReadCursorAdvanced = 6,
        HistoryPageLoaded = 7,
        PeerReadOurMessages = 8,
        PeerStatusChanged = 9,
        PeerTypingChanged = 10
    }
}
