// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Runtime.Serialization;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Repositories
{
    /// <summary>Persistent envelope for <see cref="JsonMessageRepository"/>.</summary>
    [DataContract(Name = "MessageRepositoryState", Namespace = "https://vianigram/storage/v1")]
    public sealed class MessageRepositoryState
    {
        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "messages", Order = 1)]
        public List<MessageRecord> Messages { get; set; }

        public MessageRepositoryState()
        {
            Version = 1;
            Messages = new List<MessageRecord>();
        }
    }
}
