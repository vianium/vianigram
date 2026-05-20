// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Toggle the local microphone for an active call (VoIP-engine-local).

using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Application.UseCases
{
    /// <summary>
    /// Toggle the local microphone mute state for the call identified by
    /// <see cref="CallId"/>. Routed to the local VoIP media plane; never
    /// hits MTProto.
    /// </summary>
    public sealed class SetMutedCommand
    {
        public CallId CallId { get; private set; }
        public bool Muted { get; private set; }

        public SetMutedCommand(CallId callId, bool muted)
        {
            CallId = callId;
            Muted = muted;
        }
    }
}
