// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Chats.Domain.Events;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="IChatsApi.DialogChanged"/> whenever the
    /// catalog or a single dialog mutates. Mirrors the kernel-bus events
    /// (<see cref="DialogAdded"/>, <see cref="DialogUpdated"/>, <see cref="DialogRemoved"/>)
    /// in a CLR-event shape so XAML/UI layers that don't take an <c>IEventBus</c>
    /// dependency can still subscribe.
    /// </summary>
    public sealed class DialogChangedEventArgs : EventArgs
    {
        public enum ChangeReason
        {
            Added = 0,
            Updated = 1,
            Removed = 2,
            ListSynced = 3
        }

        public ChangeReason Reason { get; private set; }
        public PeerId Peer { get; private set; }       // null when Reason == ListSynced
        public ChangeKind? FacetChanged { get; private set; }
        public DateTime At { get; private set; }

        public DialogChangedEventArgs(ChangeReason reason, PeerId peer, ChangeKind? facetChanged, DateTime at)
        {
            Reason = reason;
            Peer = peer;
            FacetChanged = facetChanged;
            At = at;
        }
    }
}
