// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Contacts.Domain.ValueObjects;

namespace Vianigram.Contacts.Application.UseCases
{
    /// <summary>
    /// Bulk import device contacts via <c>contacts.importContacts#2c800be5</c>.
    /// The handler will chunk on the wire; this command holds the full set.
    /// </summary>
    public sealed class ImportContactsCommand
    {
        public IList<ContactImportRequest> Requests { get; private set; }

        public ImportContactsCommand(IList<ContactImportRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException("requests");
            Requests = requests;
        }
    }
}
