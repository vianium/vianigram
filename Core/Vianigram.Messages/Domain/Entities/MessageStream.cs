// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Domain.Entities
{
    /// <summary>
    /// Per-peer message stream — the aggregate root of the Messages bounded
    /// context. Holds an ordered (descending by id) view of cached messages,
    /// plus optimistic pending sends that have not yet received a server id.
    ///
    /// Ordering invariant: confirmed messages sort by server id descending;
    /// pending messages sort by their (negative) client-temp id descending,
    /// which keeps newest-first overall because pending ids are always
    /// numerically greater than not-yet-existing future server ids on the
    /// "newest" axis only after we re-key on confirm. We approximate this by
    /// keeping pendings at the head of the list until they confirm.
    /// </summary>
    public sealed class MessageStream
    {
        private readonly List<Message> _messages;

        public MessageStream(string peerKey)
        {
            if (!ValueObjects.PeerKey.IsValid(peerKey)) throw new ArgumentException("invalid peerKey", "peerKey");
            this.PeerKey = peerKey;
            _messages = new List<Message>();
            HasMoreOlder = true;
        }

        public string PeerKey { get; private set; }

        public IList<Message> Messages
        {
            get { return _messages; }
        }

        public bool HasMoreOlder { get; private set; }

        public long? OldestKnownMessageId { get; private set; }

        // ---------- Mutations ----------

        /// <summary>
        /// Insert a page of older history (server-confirmed messages) at the
        /// tail of the stream. <paramref name="page"/> is expected to be
        /// already sorted newest-first.
        /// </summary>
        public void AppendOlderPage(IList<Message> page, bool hasMore)
        {
            if (page == null) throw new ArgumentNullException("page");

            for (int i = 0; i < page.Count; i++)
            {
                var m = page[i];
                if (m == null) continue;
                if (!m.Id.IsConfirmed) continue;
                if (FindIndexByServerId(m.Id.ServerId) >= 0) continue;
                _messages.Add(m);
            }

            SortByIdDescendingStable();
            RecomputeOldest();
            HasMoreOlder = hasMore;
        }

        /// <summary>
        /// Optimistically prepend a pending outgoing message before the
        /// network call is issued. M1 (mandatory optimistic UI) requires this
        /// to complete in O(1) and emit synchronously upstream.
        /// </summary>
        public void InsertOptimistic(Message pendingMsg)
        {
            if (pendingMsg == null) throw new ArgumentNullException("pendingMsg");
            if (pendingMsg.Id.IsConfirmed) throw new ArgumentException("expected pending id", "pendingMsg");
            _messages.Insert(0, pendingMsg);
        }

        /// <summary>
        /// Promote a pending optimistic message to confirmed once the server
        /// ACK lands. Body becomes effectively immutable from this point —
        /// future changes are edits/deletes, modelled as separate events.
        /// </summary>
        public void ConfirmOptimistic(long clientTempId, long serverId, DateTime serverDate)
        {
            int idx = FindIndexByPendingId(clientTempId);
            if (idx < 0) return;

            // Possible race: server might have echoed the same message via
            // another updates path before our handler reached this point.
            int existing = FindIndexByServerId(serverId);
            if (existing >= 0 && existing != idx)
            {
                // Drop the now-duplicate pending placeholder and keep the
                // server-delivered one.
                _messages.RemoveAt(idx);
                return;
            }

            _messages[idx].ConfirmSent(serverId, serverDate);
            SortByIdDescendingStable();
        }

        /// <summary>
        /// Append a single confirmed message — used both for newly arrived
        /// inbound messages and for late-binding outbound paths that bypass
        /// the optimistic insert.
        /// </summary>
        public void Append(Message m)
        {
            if (m == null) throw new ArgumentNullException("m");
            if (!m.Id.IsConfirmed) throw new ArgumentException("expected confirmed message", "m");

            if (FindIndexByServerId(m.Id.ServerId) >= 0) return;
            _messages.Insert(0, m);
            SortByIdDescendingStable();
            RecomputeOldest();
        }

        public void Apply(Events.MessageEditEvent edit)
        {
            if (edit == null) throw new ArgumentNullException("edit");
            int idx = FindIndexByServerId(edit.MessageId);
            if (idx < 0) return;
            _messages[idx].Edit(edit.NewContent, edit.EditedAt);
        }

        public void Apply(Events.MessageDeleteEvent del)
        {
            if (del == null) throw new ArgumentNullException("del");
            int idx = FindIndexByServerId(del.MessageId);
            if (idx < 0) return;
            _messages[idx].MarkDeleted();
        }

        // ---------- Reads ----------

        public Message FindByServerId(long serverId)
        {
            int idx = FindIndexByServerId(serverId);
            return idx < 0 ? null : _messages[idx];
        }

        public Message FindByClientTempId(long clientTempId)
        {
            int idx = FindIndexByPendingId(clientTempId);
            return idx < 0 ? null : _messages[idx];
        }

        // ---------- Helpers ----------

        private int FindIndexByServerId(long serverId)
        {
            for (int i = 0; i < _messages.Count; i++)
            {
                var id = _messages[i].Id;
                if (id.IsConfirmed && id.ServerId == serverId) return i;
            }
            return -1;
        }

        private int FindIndexByPendingId(long clientTempId)
        {
            for (int i = 0; i < _messages.Count; i++)
            {
                var id = _messages[i].Id;
                if (!id.IsConfirmed && id.ClientTempId.HasValue && id.ClientTempId.Value == clientTempId)
                    return i;
            }
            return -1;
        }

        private void SortByIdDescendingStable()
        {
            _messages.Sort(CompareDesc);
        }

        private static int CompareDesc(Message a, Message b)
        {
            // Pending (negative) ids sort to the head per the comment above.
            // We rank: confirmed > pending, then within each group descending by
            // numeric id (server id for confirmed, client temp id for pending).
            bool aConf = a.Id.IsConfirmed;
            bool bConf = b.Id.IsConfirmed;
            if (aConf != bConf) return aConf ? 1 : -1; // pending first
            long la = aConf ? a.Id.ServerId : (a.Id.ClientTempId.HasValue ? a.Id.ClientTempId.Value : 0);
            long lb = bConf ? b.Id.ServerId : (b.Id.ClientTempId.HasValue ? b.Id.ClientTempId.Value : 0);
            if (la == lb) return 0;
            return la < lb ? 1 : -1;
        }

        private void RecomputeOldest()
        {
            long? oldest = null;
            for (int i = 0; i < _messages.Count; i++)
            {
                var id = _messages[i].Id;
                if (!id.IsConfirmed) continue;
                if (!oldest.HasValue || id.ServerId < oldest.Value) oldest = id.ServerId;
            }
            OldestKnownMessageId = oldest;
        }
    }
}
