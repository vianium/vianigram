// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Toggle the speakerphone routing for an active call (VoIP-engine-local).

using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Application.UseCases
{
    /// <summary>
    /// Toggle speakerphone routing for the call identified by
    /// <see cref="CallId"/>. Routed to the local VoIP media plane; never
    /// hits MTProto.
    /// </summary>
    public sealed class SetSpeakerCommand
    {
        public CallId CallId { get; private set; }
        public bool On { get; private set; }

        public SetSpeakerCommand(CallId callId, bool on)
        {
            CallId = callId;
            On = on;
        }
    }
}
