// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Calls.Application.UseCases
{
    /// <summary>
    /// Apply an inbound <c>updatePhoneCall</c> delivered by
    /// <c>Vianigram.Sync</c>. Carries the raw TL bytes for the embedded
    /// <c>PhoneCall</c> constructor (one of <c>phoneCallEmpty</c>,
    /// <c>phoneCallWaiting</c>, <c>phoneCallRequested</c>,
    /// <c>phoneCallAccepted</c>, <c>phoneCall</c>,
    /// <c>phoneCallDiscarded</c>); the handler decodes and dispatches.
    ///
    /// <para>Sync is responsible for unwrapping the outer
    /// <c>updatePhoneCall</c> envelope and calling this command with just
    /// the body. The side that fetches additional context (peer access hash)
    /// from <c>Vianigram.Contacts</c> is currently stubbed; the smoke harness
    /// passes synthetic bytes.</para>
    /// </summary>
    public sealed class UpdateCallStateCommand
    {
        public byte[] PhoneCallTlBytes { get; private set; }

        public UpdateCallStateCommand(byte[] phoneCallTlBytes)
        {
            PhoneCallTlBytes = phoneCallTlBytes;
        }
    }
}
