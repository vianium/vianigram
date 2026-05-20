// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Runtime.Serialization;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Repositories
{
    /// <summary>Persistent envelope for <see cref="JsonDialogRepository"/>.</summary>
    [DataContract(Name = "DialogRepositoryState", Namespace = "https://vianigram/storage/v1")]
    public sealed class DialogRepositoryState
    {
        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "dialogs", Order = 1)]
        public List<DialogRecord> Dialogs { get; set; }

        public DialogRepositoryState()
        {
            Version = 1;
            Dialogs = new List<DialogRecord>();
        }
    }
}
