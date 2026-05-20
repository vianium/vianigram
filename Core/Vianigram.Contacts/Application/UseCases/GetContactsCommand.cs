// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Local-cache snapshot for IContactsApi.GetContactsAsync.

namespace Vianigram.Contacts.Application.UseCases
{
    /// <summary>
    /// Marker command for "read the local contacts snapshot — no wire" — the
    /// counterpart to <see cref="SyncContactsCommand"/> which round-trips
    /// <c>contacts.getContacts</c>. Carries no parameters today; the singleton
    /// <see cref="Default"/> instance is reused by every call.
    /// </summary>
    public sealed class GetContactsCommand
    {
        public static readonly GetContactsCommand Default = new GetContactsCommand();
        private GetContactsCommand() { }
    }
}
