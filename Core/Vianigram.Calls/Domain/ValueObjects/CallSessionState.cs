// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// Lifecycle state for a <see cref="Vianigram.Calls.Domain.Entities.CallSession"/>.
    ///
    /// <para><b>Outgoing direction</b> (we initiated):
    /// <c>Requesting → Waiting → Ringing → Active → Discarded</c></para>
    ///
    /// <para><b>Incoming direction</b> (peer initiated, we received via
    /// <c>updatePhoneCall</c>):
    /// <c>Receiving → Pending → Active → Discarded</c></para>
    ///
    /// <para>Mirrors Telegram's <c>phoneCall*</c> family:
    /// <list type="bullet">
    ///   <item><c>Requesting</c> ↔ local has issued <c>phone.requestCall</c>; awaiting server ack.</item>
    ///   <item><c>Waiting</c>    ↔ server returned <c>phoneCallWaiting</c>; peer's device has not yet acknowledged.</item>
    ///   <item><c>Ringing</c>    ↔ outbound: peer's device received the offer (<c>phoneCallWaiting</c> with <c>receive_date</c> set, or <c>phoneCallAccepted</c> in flight).</item>
    ///   <item><c>Receiving</c>  ↔ inbound: <c>updatePhoneCall</c> with <c>phoneCallRequested</c> just arrived; ringer is firing.</item>
    ///   <item><c>Pending</c>    ↔ inbound: local user accepted; <c>phone.acceptCall</c> in flight.</item>
    ///   <item><c>Active</c>     ↔ <c>phoneCall</c> received with key fingerprint and connections; voip media plane started.</item>
    ///   <item><c>Discarded</c>  ↔ terminal — either side issued <c>phone.discardCall</c> or a security abort wiped the call.</item>
    /// </list>
    /// </para>
    /// </summary>
    public enum CallSessionState
    {
        /// <summary>Outbound — local issued <c>phone.requestCall</c>; awaiting server.</summary>
        Requesting = 0,
        /// <summary>Outbound — server has the request, peer not yet notified.</summary>
        Waiting = 1,
        /// <summary>Outbound — peer's device received the offer and is alerting the user.</summary>
        Ringing = 2,
        /// <summary>Inbound — local received <c>phoneCallRequested</c>; ringer alerting.</summary>
        Receiving = 3,
        /// <summary>Inbound — local accepted; <c>phone.acceptCall</c> in flight, awaiting <c>phoneCall</c>.</summary>
        Pending = 4,
        /// <summary>Outbound - peer accepted and local is completing <c>phone.confirmCall</c>.</summary>
        Confirming = 5,
        /// <summary>Both sides - <c>phoneCall</c> established, native media/signaling is connecting.</summary>
        MediaConnecting = 6,
        /// <summary>Both sides — <c>phoneCall</c> arrived; key + connections are in hand; media plane started.</summary>
        Active = 7,
        /// <summary>Terminal. <c>phone.discardCall</c> issued or peer-discarded received. Voip plane stopped.</summary>
        Discarded = 8
    }
}
