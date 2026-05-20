// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Runtime.Serialization;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Repositories
{
    /// <summary>Persistent envelope for <see cref="JsonAuthKeyStore"/>.</summary>
    [DataContract(Name = "AuthKeyStoreState", Namespace = "https://vianigram/storage/v1")]
    public sealed class AuthKeyStoreState
    {
        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "records", Order = 1)]
        public List<AuthKeyRecord> Records { get; set; }

        public AuthKeyStoreState()
        {
            Version = 1;
            Records = new List<AuthKeyRecord>();
        }
    }
}
