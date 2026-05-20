// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Runtime.Serialization;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Repositories
{
    /// <summary>Persistent envelope for <see cref="JsonSyncStateRepository"/>.</summary>
    [DataContract(Name = "SyncStateRepositoryState", Namespace = "https://vianigram/storage/v1")]
    public sealed class SyncStateRepositoryState
    {
        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "state", Order = 1)]
        public SyncStateRecord State { get; set; }

        public SyncStateRepositoryState()
        {
            Version = 1;
            State = new SyncStateRecord();
        }
    }
}
