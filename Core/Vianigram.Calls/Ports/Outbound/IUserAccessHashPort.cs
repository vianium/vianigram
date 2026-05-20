// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IUserAccessHashPort.cs — Vianigram.Calls.Ports.Outbound
//
// Lookup port the Calls context uses to resolve the per-user access_hash
// required by phone.requestCall (TL inputUser carries (id, access_hash)).
// The composition root binds this to the process-wide IPeerCache (via
// Vianigram.Composition.Infrastructure.PeerAccessHashAdapter), so any peer
// the user has interacted with — observed in a dialog, message sender, or
// push payload — yields the right hash without an extra round-trip.
//
// Returns 0 when the user id has not been observed yet; the caller then
// has to either fetch a fresh user record (users.getUsers) or fail the
// call request gracefully (server replies USER_ID_INVALID otherwise).

namespace Vianigram.Calls.Ports.Outbound
{
    public interface IUserAccessHashPort
    {
        long GetUserAccessHash(long userId);
    }
}
