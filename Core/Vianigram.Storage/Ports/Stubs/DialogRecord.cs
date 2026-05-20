// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Runtime.Serialization;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>STUB DTO for <see cref="IDialogRepository"/>.</summary>
    [DataContract(Name = "DialogRecord", Namespace = "https://vianigram/storage/v1")]
    public sealed class DialogRecord
    {
        [DataMember(Name = "peerId", Order = 0)]
        public long PeerId { get; set; }

        [DataMember(Name = "title", Order = 1)]
        public string Title { get; set; }

        [DataMember(Name = "lastMessagePreview", Order = 2)]
        public string LastMessagePreview { get; set; }

        [DataMember(Name = "lastMessageDateUnix", Order = 3)]
        public long LastMessageDateUnix { get; set; }

        [DataMember(Name = "topMessageId", Order = 4)]
        public int TopMessageId { get; set; }

        [DataMember(Name = "unreadCount", Order = 5)]
        public int UnreadCount { get; set; }

        [DataMember(Name = "pinned", Order = 6)]
        public bool Pinned { get; set; }

        [DataMember(Name = "muted", Order = 7)]
        public bool Muted { get; set; }
    }
}
