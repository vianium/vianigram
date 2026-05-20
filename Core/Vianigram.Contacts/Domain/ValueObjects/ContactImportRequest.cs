// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Contacts.Domain.ValueObjects
{
    /// <summary>
    /// One row in a bulk <c>contacts.importContacts</c> request. The
    /// <see cref="ClientId"/> is a caller-allocated 64-bit handle used by the
    /// server to correlate imported entries with the device-side contact ID;
    /// it is echoed back in <see cref="ContactImportResult"/> so the client can
    /// pair successful imports with the originating row even if the user is
    /// not on Telegram (no user_id returned).
    ///
    /// Immutable.
    /// </summary>
    public sealed class ContactImportRequest
    {
        private readonly long _clientId;
        private readonly PhoneNumber _phone;
        private readonly string _firstName;
        private readonly string _lastName;

        public ContactImportRequest(long clientId, PhoneNumber phone, string firstName, string lastName)
        {
            if (phone == null) throw new ArgumentNullException("phone");
            _clientId = clientId;
            _phone = phone;
            _firstName = firstName ?? string.Empty;
            _lastName = lastName ?? string.Empty;
        }

        public long ClientId { get { return _clientId; } }
        public PhoneNumber Phone { get { return _phone; } }
        public string FirstName { get { return _firstName; } }
        public string LastName { get { return _lastName; } }
    }
}
