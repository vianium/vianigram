// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// UserFullResponse.cs — Vianigram.Account.Ports.Outbound
// Wire DTO for users.getFullUser — minimal projection used by GetSelf.

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Wire-level projection of <c>users.getFullUser</c>. Only the fields the
    /// <c>GetSelfHandler</c> reads are surfaced; the rest of the TL response
    /// stays inside the rpc adapter.
    /// </summary>
    public sealed class UserFullResponse
    {
        public long UserId { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Username { get; set; }
        public string Phone { get; set; }
        public string Bio { get; set; }
    }
}
