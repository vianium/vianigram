// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Contacts.Domain.ValueObjects;

namespace Vianigram.Contacts.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="IContactsApi.ContactsChanged"/>
    /// whenever the contact book or blocked set mutates. Mirrors the kernel
    /// bus events (<c>ContactImported</c>, <c>ContactUpdated</c>,
    /// <c>ContactRemoved</c>, <c>UserBlocked</c>, <c>UserUnblocked</c>,
    /// <c>ContactsSynced</c>) in a CLR-event shape so XAML/UI layers that
    /// don't take an <c>IEventBus</c> dependency can still subscribe.
    /// </summary>
    public sealed class ContactsChangedEventArgs : EventArgs
    {
        public enum ChangeReason
        {
            ContactImported = 0,
            ContactUpdated = 1,
            ContactRemoved = 2,
            UserBlocked = 3,
            UserUnblocked = 4,
            ListSynced = 5
        }

        public ChangeReason Reason { get; private set; }
        /// <summary>Affected user. Null when <see cref="Reason"/> is <see cref="ChangeReason.ListSynced"/>.</summary>
        public UserId? User { get; private set; }
        public DateTime At { get; private set; }

        public ContactsChangedEventArgs(ChangeReason reason, UserId? user, DateTime at)
        {
            Reason = reason;
            User = user;
            At = at;
        }
    }
}
