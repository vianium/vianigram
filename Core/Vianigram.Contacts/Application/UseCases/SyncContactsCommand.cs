// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Contacts.Application.UseCases
{
    /// <summary>
    /// Full sync from the server (<c>contacts.getContacts#5dd69e12</c>). The
    /// hash is the saved-contacts hash from the previous sync, used by the
    /// server to short-circuit with <c>contacts.contactsNotModified</c> when
    /// nothing changed.
    /// </summary>
    public sealed class SyncContactsCommand
    {
        public long Hash { get; private set; }
        public static readonly SyncContactsCommand Initial = new SyncContactsCommand(0L);

        public SyncContactsCommand(long hash)
        {
            Hash = hash;
        }
    }
}
