// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Application.UseCases
{
    /// <summary>
    /// Discard a call — calls <c>phone.discardCall</c>, stops the VoIP
    /// media plane, and stages <see cref="Domain.Events.CallDiscarded"/>.
    /// Reused by both hangup (Active -&gt; Discarded) and reject
    /// (Receiving -&gt; Discarded) flows.
    /// </summary>
    public sealed class DiscardCallCommand
    {
        public CallId CallId { get; private set; }
        public DiscardReason Reason { get; private set; }

        public DiscardCallCommand(CallId callId, DiscardReason reason)
        {
            CallId = callId;
            Reason = reason;
        }

        public DiscardCallCommand(CallId callId)
            : this(callId, DiscardReason.Hangup)
        {
        }
    }
}
