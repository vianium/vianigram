// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Plaintext-in-memory record of a per-DC auth_key plus its session
    /// metadata. The storage adapter encrypts at rest; callers never see
    /// ciphertext through this type.
    ///
    /// See principles.md §M3 (Key material isolation) — these bytes only live
    /// long enough to be handed to the native MtProtoChannel.
    /// </summary>
    public sealed class AuthKeyRecord
    {
        /// <summary>256-byte raw auth_key. Adapters encrypt before persisting.</summary>
        public byte[] AuthKey { get; set; }

        /// <summary>Lower 64 bits of SHA1(auth_key).</summary>
        public ulong AuthKeyId { get; set; }

        /// <summary>Server-assigned salt (rotates ~every 30 minutes).</summary>
        public long ServerSalt { get; set; }

        /// <summary>Seconds offset between server clock and local clock.</summary>
        public int ServerTimeOffset { get; set; }
    }
}
