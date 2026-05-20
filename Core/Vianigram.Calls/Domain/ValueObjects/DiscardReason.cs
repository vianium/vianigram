// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// Why a call ended. Mirrors the TL <c>PhoneCallDiscardReason</c> family:
    /// <list type="bullet">
    ///   <item><c>phoneCallDiscardReasonHangup#57adc690</c> -&gt; <see cref="Hangup"/></item>
    ///   <item><c>phoneCallDiscardReasonDisconnect#e095c1a0</c> -&gt; <see cref="Disconnect"/></item>
    ///   <item><c>phoneCallDiscardReasonMissed#85e42301</c> -&gt; <see cref="Missed"/></item>
    ///   <item><c>phoneCallDiscardReasonBusy#faf7e8c9</c> -&gt; <see cref="Busy"/></item>
    /// </list>
    ///
    /// Plus a few client-only reasons that never cross the wire — they are
    /// staged on <see cref="Domain.Events.CallDiscarded"/> for UI but
    /// translate to <see cref="Hangup"/> when issuing
    /// <c>phone.discardCall</c>.
    /// </summary>
    public enum DiscardReason
    {
        /// <summary>Local user pressed end / peer pressed end. Wire: hangup.</summary>
        Hangup = 0,
        /// <summary>Network gave up; the media plane lost peer reachability. Wire: disconnect.</summary>
        Disconnect = 1,
        /// <summary>Call rang but was not answered before timeout. Wire: missed.</summary>
        Missed = 2,
        /// <summary>Receiver was already in another call. Wire: busy.</summary>
        Busy = 3,
        /// <summary>Local protocol/security check aborted the call (fingerprint mismatch, malformed
        /// constructor, capability mismatch). Wire-translated to hangup.</summary>
        ProtocolError = 4,
        /// <summary>Account logged out / device suspended past the grace window. Wire-translated to hangup.</summary>
        LocalShutdown = 5
    }
}
