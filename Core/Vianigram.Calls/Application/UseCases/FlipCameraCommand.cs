// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Flip front/rear camera for an active video call (VoIP-engine-local).

using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Application.UseCases
{
    /// <summary>
    /// Switch between front and rear cameras for the active video call
    /// identified by <see cref="CallId"/>. Routed to the local VoIP media
    /// plane; never hits MTProto.
    /// </summary>
    public sealed class FlipCameraCommand
    {
        public CallId CallId { get; private set; }

        public FlipCameraCommand(CallId callId)
        {
            CallId = callId;
        }
    }
}
