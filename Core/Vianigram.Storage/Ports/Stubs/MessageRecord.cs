// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Runtime.Serialization;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>STUB DTO for <see cref="IMessageRepository"/>.</summary>
    [DataContract(Name = "MessageRecord", Namespace = "https://vianigram/storage/v1")]
    public sealed class MessageRecord
    {
        [DataMember(Name = "peerId", Order = 0)]
        public long PeerId { get; set; }

        [DataMember(Name = "messageId", Order = 1)]
        public int MessageId { get; set; }

        [DataMember(Name = "fromUserId", Order = 2)]
        public long FromUserId { get; set; }

        [DataMember(Name = "dateUnix", Order = 3)]
        public long DateUnix { get; set; }

        [DataMember(Name = "body", Order = 4)]
        public string Body { get; set; }

        [DataMember(Name = "outgoing", Order = 5)]
        public bool Outgoing { get; set; }

        [DataMember(Name = "readByMe", Order = 6)]
        public bool ReadByMe { get; set; }

        [DataMember(Name = "edited", Order = 7)]
        public bool Edited { get; set; }
    }
}
