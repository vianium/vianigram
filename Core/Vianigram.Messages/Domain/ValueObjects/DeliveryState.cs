// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Domain.ValueObjects
{
    /// <summary>
    /// Lifecycle of an outgoing message from optimistic insert to terminal state.
    /// Inbound (incoming) messages are created directly in <see cref="Delivered"/>.
    /// </summary>
    public enum DeliveryState
    {
        /// <summary>Optimistically inserted; awaiting server ACK.</summary>
        Sending = 0,

        /// <summary>Server returned a confirmed ID; not yet delivered to peer.</summary>
        Sent = 1,

        /// <summary>Delivered to peer (or applied locally for inbound messages).</summary>
        Delivered = 2,

        /// <summary>Peer (or local user, for inbound) marked as read.</summary>
        Read = 3,

        /// <summary>Send attempt terminally failed; retry affordance shown in UI.</summary>
        Failed = 4
    }
}
