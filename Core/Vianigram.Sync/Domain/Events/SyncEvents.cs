// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Sync.Domain.ValueObjects;

namespace Vianigram.Sync.Domain.Events
{
    // ---------------------------------------------------------------------
    // Sync-internal events (consumed by Sync diagnostics, telemetry, and the
    // health snapshot exposed via ISyncApi.GetHealth()).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Emitted after an UpdatesEnvelope has been fully applied to SyncState and
    /// the corresponding derived events have been published.
    ///
    /// EventCount is the number of derived events that were emitted as a side
    /// effect (NOT the count of TL Update entries — a single UpdateNewMessage
    /// produces one RemoteMessageReceived).
    /// </summary>
    public sealed class UpdatesApplied : IDomainEvent
    {
        public UpdatesApplied(int eventCount, DateTime timestampUtc)
        {
            EventCount = eventCount;
            TimestampUtc = timestampUtc;
        }

        public int EventCount { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// pts/qts/seq jump detected. Triggers a getDifference (common box) or
    /// getChannelDifference (per channel) request from the application layer.
    /// </summary>
    public sealed class GapDetected : IDomainEvent
    {
        public GapDetected(int expectedPts, int actualPts, long? channelId, DateTime timestampUtc)
        {
            ExpectedPts = expectedPts;
            ActualPts = actualPts;
            ChannelId = channelId;
            TimestampUtc = timestampUtc;
        }

        public int ExpectedPts { get; private set; }
        public int ActualPts { get; private set; }
        /// <summary>Null = common box; non-null = channel pts gap.</summary>
        public long? ChannelId { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class GapResolved : IDomainEvent
    {
        public GapResolved(long? channelId, int filledPts, DateTime timestampUtc)
        {
            ChannelId = channelId;
            FilledPts = filledPts;
            TimestampUtc = timestampUtc;
        }

        public long? ChannelId { get; private set; }
        public int FilledPts { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// Initial bootstrap completed: cursor seeded via updates.getState and any
    /// pending getDifference catch-up applied. Other contexts may now safely
    /// subscribe to derived events without missing history.
    /// </summary>
    public sealed class SyncReady : IDomainEvent
    {
        public SyncReady(DateTime timestampUtc) { TimestampUtc = timestampUtc; }
        public DateTime TimestampUtc { get; private set; }
    }

    // ---------------------------------------------------------------------
    // Derived events — Sync's primary cross-context surface (rule M5/M6).
    // Other contexts (Messages, Chats, Contacts, Notifications) subscribe to
    // these and never to the underlying TL types.
    // ---------------------------------------------------------------------

    public sealed class RemoteMessageReceived : IDomainEvent
    {
        public RemoteMessageReceived(string peerKey, MessageDto message, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            Message = message;
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public MessageDto Message { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// A message previously surfaced by <see cref="RemoteMessageReceived"/>
    /// has been edited. Carries the same <see cref="MessageDto"/> shape as
    /// Received so downstream consumers can swap the rendered bubble without
    /// an extra fetch.
    /// </summary>
    public sealed class RemoteMessageEdited : IDomainEvent
    {
        public RemoteMessageEdited(string peerKey, MessageDto message, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            Message = message;
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public MessageDto Message { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class RemoteMessageDeleted : IDomainEvent
    {
        public RemoteMessageDeleted(string peerKey, IList<int> messageIds, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            MessageIds = messageIds ?? new List<int>(0);
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public IList<int> MessageIds { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// Remote read mark advanced — either by the peer ("they read mine", ByMe=false)
    /// or by another logged-in session of ours ("I read it elsewhere", ByMe=true).
    /// </summary>
    public sealed class RemoteMessageRead : IDomainEvent
    {
        public RemoteMessageRead(string peerKey, int upToMessageId, bool byMe, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            UpToMessageId = upToMessageId;
            ByMe = byMe;
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public int UpToMessageId { get; private set; }
        public bool ByMe { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class RemoteUserStatusChanged : IDomainEvent
    {
        public RemoteUserStatusChanged(long userId, UserStatusKind status, DateTime? wasOnlineUtc, DateTime timestampUtc)
        {
            UserId = userId;
            Status = status;
            WasOnlineUtc = wasOnlineUtc;
            TimestampUtc = timestampUtc;
        }

        public long UserId { get; private set; }
        public UserStatusKind Status { get; private set; }
        public DateTime? WasOnlineUtc { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class RemoteUserTypingChanged : IDomainEvent
    {
        public RemoteUserTypingChanged(string peerKey, long userId, TypingActionKind action, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            UserId = userId;
            Action = action;
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public long UserId { get; private set; }
        public TypingActionKind Action { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// updateMessageID — pairs a server-issued message id with the random_id
    /// the client tagged the optimistic send with. Messages context uses this
    /// to swap the local placeholder for the real id without an extra fetch.
    /// </summary>
    public sealed class RemoteMessageIdAssigned : IDomainEvent
    {
        public RemoteMessageIdAssigned(int serverMessageId, long randomId, DateTime timestampUtc)
        {
            ServerMessageId = serverMessageId;
            RandomId = randomId;
            TimestampUtc = timestampUtc;
        }

        public int ServerMessageId { get; private set; }
        public long RandomId { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class RemoteUserNameChanged : IDomainEvent
    {
        public RemoteUserNameChanged(long userId, string firstName, string lastName, string username, DateTime timestampUtc)
        {
            UserId = userId;
            FirstName = firstName ?? string.Empty;
            LastName = lastName ?? string.Empty;
            Username = username ?? string.Empty;
            TimestampUtc = timestampUtc;
        }

        public long UserId { get; private set; }
        public string FirstName { get; private set; }
        public string LastName { get; private set; }
        public string Username { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class RemoteUserPhoneChanged : IDomainEvent
    {
        public RemoteUserPhoneChanged(long userId, string phone, DateTime timestampUtc)
        {
            UserId = userId;
            Phone = phone ?? string.Empty;
            TimestampUtc = timestampUtc;
        }

        public long UserId { get; private set; }
        public string Phone { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class RemoteUserPhotoChanged : IDomainEvent
    {
        public RemoteUserPhotoChanged(long userId, ProfilePhotoSummary photo, DateTime timestampUtc)
        {
            UserId = userId;
            Photo = photo ?? ProfilePhotoSummary.Empty;
            TimestampUtc = timestampUtc;
        }

        public long UserId { get; private set; }
        public ProfilePhotoSummary Photo { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class RemoteNotifySettingsChanged : IDomainEvent
    {
        public RemoteNotifySettingsChanged(string peerKey, bool? showPreviews, bool? silent, int muteUntil, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            ShowPreviews = showPreviews;
            Silent = silent;
            MuteUntil = muteUntil;
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public bool? ShowPreviews { get; private set; }
        public bool? Silent { get; private set; }
        public int MuteUntil { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// The reaction set on a message changed.
    /// We surface peer + message id; the actual emoji aggregation is
    /// deferred (the wire payload requires decoding the full
    /// <c>MessageReactions</c> TL sub-tree).
    /// </summary>
    public sealed class RemoteMessageReactionsChanged : IDomainEvent
    {
        public RemoteMessageReactionsChanged(string peerKey, int messageId, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            MessageId = messageId;
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public int MessageId { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }
}
