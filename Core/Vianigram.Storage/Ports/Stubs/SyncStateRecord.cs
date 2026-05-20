// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Runtime.Serialization;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>STUB DTO for <see cref="ISyncStateRepository"/>.</summary>
    [DataContract(Name = "SyncStateRecord", Namespace = "https://vianigram/storage/v1")]
    public sealed class SyncStateRecord
    {
        /// <summary>Telegram <c>updates.getState.pts</c>.</summary>
        [DataMember(Name = "pts", Order = 0)]
        public int Pts { get; set; }

        /// <summary>Telegram <c>updates.getState.qts</c> (secret chats).</summary>
        [DataMember(Name = "qts", Order = 1)]
        public int Qts { get; set; }

        /// <summary>Server-side <c>date</c> from the last successful sync.</summary>
        [DataMember(Name = "dateUnix", Order = 2)]
        public long DateUnix { get; set; }

        /// <summary>Last applied <c>seq</c> value.</summary>
        [DataMember(Name = "seq", Order = 3)]
        public int Seq { get; set; }

        /// <summary>Unix seconds at the time of last successful sync.</summary>
        [DataMember(Name = "lastSyncUnix", Order = 4)]
        public long LastSyncUnix { get; set; }
    }
}
