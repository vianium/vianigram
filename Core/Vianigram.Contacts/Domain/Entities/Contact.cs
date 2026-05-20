// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Contacts.Domain.ValueObjects;

namespace Vianigram.Contacts.Domain.Entities
{
    /// <summary>
    /// One contact entry. Identity is by <see cref="UserId"/>.
    ///
    /// Carries the canonical user-facing fields (phone, first/last name,
    /// optional username) plus two server-asserted booleans:
    ///
    ///   * <see cref="IsMutual"/> — Telegram says the contact also has us in
    ///     their address book. Mirrored, never computed locally.
    ///   * <see cref="IsBlocked"/> — convenience flag projecting whether this
    ///     user appears in the blocked sub-collection of the same
    ///     <see cref="Vianigram.Contacts.Domain.Entities.ContactBook"/>.
    ///     The aggregate keeps both views in sync.
    ///
    /// Mutable in narrow ways (see <see cref="ApplyServerUpdate"/>) so callers
    /// can re-hydrate from a server sync without rebuilding the aggregate. All
    /// other transitions (block / unblock) live on the aggregate root.
    /// </summary>
    public sealed class Contact
    {
        private readonly UserId _userId;
        private string _phone;
        private string _firstName;
        private string _lastName;
        private string _username;
        private bool _isMutual;
        private bool _isBlocked;

        public Contact(
            UserId userId,
            string phone,
            string firstName,
            string lastName,
            string username,
            bool isMutual,
            bool isBlocked)
        {
            _userId = userId;
            _phone = phone ?? string.Empty;
            _firstName = firstName ?? string.Empty;
            _lastName = lastName ?? string.Empty;
            _username = username ?? string.Empty;
            _isMutual = isMutual;
            _isBlocked = isBlocked;
        }

        public UserId UserId { get { return _userId; } }
        public string Phone { get { return _phone; } }
        public string FirstName { get { return _firstName; } }
        public string LastName { get { return _lastName; } }
        public string Username { get { return _username; } }
        public bool IsMutual { get { return _isMutual; } }
        public bool IsBlocked { get { return _isBlocked; } }

        /// <summary>Display string for UI (first + last, trimmed). Falls back to username then phone.</summary>
        public string DisplayName
        {
            get
            {
                string fn = _firstName ?? string.Empty;
                string ln = _lastName ?? string.Empty;
                string composed = (fn + " " + ln).Trim();
                if (composed.Length > 0) return composed;
                if (!string.IsNullOrEmpty(_username)) return "@" + _username;
                return _phone ?? string.Empty;
            }
        }

        /// <summary>
        /// Bulk in-place refresh from a server sync. Returns true iff any
        /// observable field changed. Aggregate root uses the return value to
        /// decide whether to stage a <c>ContactUpdated</c> domain event.
        /// </summary>
        public bool ApplyServerUpdate(string phone, string firstName, string lastName, string username, bool isMutual)
        {
            bool changed = false;
            string p = phone ?? string.Empty;
            string fn = firstName ?? string.Empty;
            string ln = lastName ?? string.Empty;
            string un = username ?? string.Empty;

            if (!string.Equals(_phone, p, StringComparison.Ordinal)) { _phone = p; changed = true; }
            if (!string.Equals(_firstName, fn, StringComparison.Ordinal)) { _firstName = fn; changed = true; }
            if (!string.Equals(_lastName, ln, StringComparison.Ordinal)) { _lastName = ln; changed = true; }
            if (!string.Equals(_username, un, StringComparison.Ordinal)) { _username = un; changed = true; }
            if (_isMutual != isMutual) { _isMutual = isMutual; changed = true; }
            return changed;
        }

        internal void SetBlocked(bool isBlocked)
        {
            _isBlocked = isBlocked;
        }
    }
}
