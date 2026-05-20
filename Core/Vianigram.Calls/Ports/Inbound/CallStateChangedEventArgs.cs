// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="ICallsApi.StateChanged"/>
    /// whenever a call session transitions through its lifecycle. Mirrors
    /// the kernel-bus events (<c>CallRequested</c>, <c>CallAccepted</c>,
    /// <c>CallActive</c>, <c>CallDiscarded</c>, <c>CallStateChanged</c>) in
    /// a CLR-event shape so XAML/UI layers that don't take an
    /// <c>IEventBus</c> dependency can still subscribe.
    /// </summary>
    public sealed class CallStateChangedEventArgs : EventArgs
    {
        public enum ChangeReason
        {
            Requested = 0,
            Accepted = 1,
            Active = 2,
            Discarded = 3,
            StateChanged = 4
        }

        public ChangeReason Reason { get; private set; }
        public CallId CallId { get; private set; }
        public CallSessionState State { get; private set; }
        public DateTime At { get; private set; }

        public CallStateChangedEventArgs(ChangeReason reason, CallId callId, CallSessionState state, DateTime at)
        {
            Reason = reason;
            CallId = callId;
            State = state;
            At = at;
        }
    }
}
