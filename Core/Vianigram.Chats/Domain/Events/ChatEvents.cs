// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Chats.Domain.Events
{
    /// <summary>
    /// Discriminator for which facet of a Dialog changed. Lets subscribers cheaply
    /// filter without comparing snapshots — UI can ignore Photo and react to LastMessage.
    /// </summary>
    public enum ChangeKind
    {
        Title = 0,
        Photo = 1,
        UnreadCount = 2,
        Pinned = 3,
        Muted = 4,
        LastMessage = 5,
        Archived = 6
    }

    /// <summary>
    /// Emitted when a brand-new Dialog enters the catalog (peer started a chat,
    /// joined a channel, was just synced for the first time).
    /// </summary>
    public sealed class DialogAdded : IDomainEvent
    {
        public PeerId Peer { get; private set; }
        public DateTime At { get; private set; }

        public DialogAdded(PeerId peer, DateTime at)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when an existing Dialog mutates one of its tracked facets.
    /// The <see cref="ChangeKind"/> tells subscribers which facet — they can
    /// re-read the aggregate via <see cref="Vianigram.Chats.Ports.Outbound.IDialogRepository"/>
    /// if they need the new value.
    /// </summary>
    public sealed class DialogUpdated : IDomainEvent
    {
        public PeerId Peer { get; private set; }
        public ChangeKind Change { get; private set; }
        public DateTime At { get; private set; }

        public DialogUpdated(PeerId peer, ChangeKind change, DateTime at)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
            Change = change;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a Dialog leaves the catalog (left a chat, deleted a conversation,
    /// blocked a peer, account logged out for that peer).
    /// </summary>
    public sealed class DialogRemoved : IDomainEvent
    {
        public PeerId Peer { get; private set; }
        public DateTime At { get; private set; }

        public DialogRemoved(PeerId peer, DateTime at)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
            At = at;
        }
    }

    /// <summary>
    /// Emitted once at the end of a successful Load/Refresh cycle. Carries the
    /// total dialog count after sync so UI/Sync can pin telemetry and validate
    /// expectations.
    /// </summary>
    public sealed class DialogListSynced : IDomainEvent
    {
        public int DialogCount { get; private set; }
        public DateTime At { get; private set; }

        public DialogListSynced(int dialogCount, DateTime at)
        {
            if (dialogCount < 0) throw new ArgumentOutOfRangeException("dialogCount", "must be >= 0");
            DialogCount = dialogCount;
            At = at;
        }
    }
}
