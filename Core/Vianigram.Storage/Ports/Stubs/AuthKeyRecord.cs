// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Runtime.Serialization;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// STUB of <c>Vianigram.Account.Ports.Outbound.AuthKeyRecord</c>. See the
    /// note on <see cref="IAuthKeyStore"/> for migration.
    /// </summary>
    [DataContract(Name = "AuthKeyRecord", Namespace = "https://vianigram/storage/v1")]
    public sealed class AuthKeyRecord
    {
        [DataMember(Name = "dcId", Order = 0)]
        public int DcId { get; set; }

        /// <summary>Telegram <c>auth_key_id</c> = lower 64 bits of SHA-1(auth_key).</summary>
        [DataMember(Name = "authKeyId", Order = 1)]
        public long AuthKeyId { get; set; }

        /// <summary>Raw 256-byte auth key material.</summary>
        [DataMember(Name = "authKey", Order = 2)]
        public byte[] AuthKey { get; set; }

        /// <summary>Server salt at the time the record was persisted (8 bytes).</summary>
        [DataMember(Name = "serverSalt", Order = 3)]
        public byte[] ServerSalt { get; set; }

        /// <summary>Unix seconds at the time of last persistence.</summary>
        [DataMember(Name = "createdAtUnix", Order = 4)]
        public long CreatedAtUnix { get; set; }

        /// <summary>Seconds offset between Telegram server time and local time.</summary>
        [DataMember(Name = "serverTimeOffset", Order = 5)]
        public int ServerTimeOffset { get; set; }
    }
}
