// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Contacts.Domain.Events;
using Vianigram.Contacts.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Contacts.Domain.Entities
{
    /// <summary>
    /// Aggregate root for the user's address-book intersection with Telegram.
    ///
    /// Owns three collections:
    ///   * <c>contacts</c> keyed by <see cref="UserId"/>.
    ///   * <c>blocked</c> sub-set of user ids that are blocked. A user can be
    ///     blocked even if they are not in <c>contacts</c>.
    ///   * <c>lastSyncAt</c> — when the last successful <c>contacts.getContacts</c>
    ///     completed; used to short-circuit redundant syncs.
    ///
    /// Mutators stage <see cref="IDomainEvent"/> instances on a pending list so
    /// the handler / repository can drain them after the persistence write
    /// succeeds. This keeps the aggregate dependency-free and makes events
    /// transactional with the state change.
    /// </summary>
    public sealed class ContactBook
    {
        private readonly Dictionary<long, Contact> _contacts;
        private readonly HashSet<long> _blocked;
        private readonly List<IDomainEvent> _pending;
        private DateTime _lastSyncAt;

        public ContactBook()
        {
            _contacts = new Dictionary<long, Contact>();
            _blocked = new HashSet<long>();
            _pending = new List<IDomainEvent>(8);
            _lastSyncAt = DateTime.MinValue;
        }

        public DateTime LastSyncAt { get { return _lastSyncAt; } }
        public int ContactCount { get { return _contacts.Count; } }
        public int BlockedCount { get { return _blocked.Count; } }

        public IList<Contact> Snapshot()
        {
            var list = new List<Contact>(_contacts.Count);
            foreach (var kv in _contacts) list.Add(kv.Value);
            return list;
        }

        public IList<long> BlockedSnapshot()
        {
            var list = new List<long>(_blocked.Count);
            foreach (var id in _blocked) list.Add(id);
            return list;
        }

        public Contact Find(UserId id)
        {
            Contact c;
            _contacts.TryGetValue(id.Value, out c);
            return c;
        }

        public bool IsBlocked(UserId id)
        {
            return _blocked.Contains(id.Value);
        }

        /// <summary>
        /// Replace the saved-contacts set with the freshly synced list. Each
        /// row is matched against the current state and either upserted (with
        /// <see cref="ContactUpdated"/> when fields actually changed) or added
        /// (with <see cref="ContactImported"/>). Existing entries that are
        /// absent from <paramref name="incoming"/> are removed and emit
        /// <see cref="ContactRemoved"/>.
        /// </summary>
        public void ApplyServerSync(IList<Contact> incoming, DateTime at)
        {
            if (incoming == null) incoming = new Contact[0];

            var seen = new HashSet<long>();
            for (int i = 0; i < incoming.Count; i++)
            {
                Contact next = incoming[i];
                if (next == null) continue;
                seen.Add(next.UserId.Value);

                Contact existing;
                if (_contacts.TryGetValue(next.UserId.Value, out existing))
                {
                    bool changed = existing.ApplyServerUpdate(
                        next.Phone, next.FirstName, next.LastName, next.Username, next.IsMutual);
                    if (changed)
                    {
                        Stage(new ContactUpdated(existing.UserId, at));
                    }
                }
                else
                {
                    // Mirror current blocked-state into the new aggregate row so the
                    // IsBlocked projection stays consistent.
                    if (_blocked.Contains(next.UserId.Value)) next.SetBlocked(true);
                    _contacts[next.UserId.Value] = next;
                    Stage(new ContactImported(next.UserId, at));
                }
            }

            // Remove dropped contacts.
            var toRemove = new List<long>();
            foreach (var kv in _contacts)
            {
                if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                _contacts.Remove(toRemove[i]);
                Stage(new ContactRemoved(new UserId(toRemove[i]), at));
            }

            _lastSyncAt = at;
            Stage(new ContactsSynced(_contacts.Count, at));
        }

        /// <summary>Add or refresh a single contact (used by import / search-resolve flows).</summary>
        public void AddOrUpdate(Contact contact, DateTime at)
        {
            if (contact == null) throw new ArgumentNullException("contact");
            Contact existing;
            if (_contacts.TryGetValue(contact.UserId.Value, out existing))
            {
                bool changed = existing.ApplyServerUpdate(
                    contact.Phone, contact.FirstName, contact.LastName, contact.Username, contact.IsMutual);
                if (changed) Stage(new ContactUpdated(existing.UserId, at));
            }
            else
            {
                if (_blocked.Contains(contact.UserId.Value)) contact.SetBlocked(true);
                _contacts[contact.UserId.Value] = contact;
                Stage(new ContactImported(contact.UserId, at));
            }
        }

        public void Remove(UserId id, DateTime at)
        {
            if (_contacts.Remove(id.Value))
            {
                Stage(new ContactRemoved(id, at));
            }
        }

        public void Block(UserId id, DateTime at)
        {
            if (!_blocked.Add(id.Value)) return;
            Contact c;
            if (_contacts.TryGetValue(id.Value, out c)) c.SetBlocked(true);
            Stage(new UserBlocked(id, at));
        }

        public void Unblock(UserId id, DateTime at)
        {
            if (!_blocked.Remove(id.Value)) return;
            Contact c;
            if (_contacts.TryGetValue(id.Value, out c)) c.SetBlocked(false);
            Stage(new UserUnblocked(id, at));
        }

        /// <summary>Replace the blocked set wholesale (used after <c>contacts.getBlocked</c>).</summary>
        public void ApplyBlockedSync(IList<long> blockedUserIds, DateTime at)
        {
            if (blockedUserIds == null) blockedUserIds = new long[0];
            var nextSet = new HashSet<long>(blockedUserIds);

            // Newly blocked.
            foreach (var id in nextSet)
            {
                if (!_blocked.Contains(id))
                {
                    _blocked.Add(id);
                    Contact c;
                    if (_contacts.TryGetValue(id, out c)) c.SetBlocked(true);
                    Stage(new UserBlocked(new UserId(id), at));
                }
            }

            // Newly unblocked.
            var dropped = new List<long>();
            foreach (var id in _blocked)
            {
                if (!nextSet.Contains(id)) dropped.Add(id);
            }
            for (int i = 0; i < dropped.Count; i++)
            {
                _blocked.Remove(dropped[i]);
                Contact c;
                if (_contacts.TryGetValue(dropped[i], out c)) c.SetBlocked(false);
                Stage(new UserUnblocked(new UserId(dropped[i]), at));
            }
        }

        /// <summary>Drain pending domain events for the handler to publish post-persistence.</summary>
        public IList<IDomainEvent> DequeuePendingEvents()
        {
            if (_pending.Count == 0) return new IDomainEvent[0];
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        private void Stage(IDomainEvent evt)
        {
            _pending.Add(evt);
        }
    }
}
