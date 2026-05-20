// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ExportedAuthorizationResponse.cs — Vianigram.Account.Ports.Outbound
//
// Result shape returned by auth.exportAuthorization. The (id, bytes) pair is
// short-lived (server-side TTL) and meant to be immediately passed to a peer
// DC via auth.importAuthorization. Mirrors the TL constructor
// auth.exportedAuthorization#b434e2b8 id:long bytes:bytes.

namespace Vianigram.Account.Ports.Outbound
{
    public sealed class ExportedAuthorizationResponse
    {
        /// <summary>auth.exportedAuthorization.id (long).</summary>
        public long Id { get; set; }

        /// <summary>
        /// Opaque authorisation blob the peer DC validates. Always
        /// non-null on the success path; the caller treats it as a byte
        /// stream and hands it back unchanged in auth.importAuthorization.
        /// </summary>
        public byte[] Bytes { get; set; }
    }
}
