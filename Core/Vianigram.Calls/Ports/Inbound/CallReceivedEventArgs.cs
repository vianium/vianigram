// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="ICallsApi.IncomingCall"/>
    /// whenever Sync delivers a <c>phoneCallRequested</c>. The UI should
    /// surface the incoming-call screen and the ringer should fire — both
    /// downstream of <see cref="ICallsApi"/> consumers.
    /// </summary>
    public sealed class CallReceivedEventArgs : EventArgs
    {
        public CallId CallId { get; private set; }
        public long FromUserId { get; private set; }
        public bool Video { get; private set; }
        public DateTime At { get; private set; }

        public CallReceivedEventArgs(CallId callId, long fromUserId, bool video, DateTime at)
        {
            CallId = callId;
            FromUserId = fromUserId;
            Video = video;
            At = at;
        }
    }
}
