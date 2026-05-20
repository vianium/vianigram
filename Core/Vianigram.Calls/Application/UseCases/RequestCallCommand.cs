// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Calls.Application.UseCases
{
    /// <summary>
    /// Initiate a new outgoing call. Triggers <c>phone.requestCall</c> with
    /// <c>g_a_hash</c> (from the crypto vault) and the negotiated
    /// <c>phoneCallProtocol</c>.
    /// </summary>
    public sealed class RequestCallCommand
    {
        public long ParticipantUserId { get; private set; }
        public long ParticipantAccessHash { get; private set; }
        public bool Video { get; private set; }

        public RequestCallCommand(long participantUserId, long participantAccessHash, bool video)
        {
            ParticipantUserId = participantUserId;
            ParticipantAccessHash = participantAccessHash;
            Video = video;
        }

        public RequestCallCommand(long participantUserId, bool video)
            : this(participantUserId, /*accessHash*/ 0L, video)
        {
        }
    }
}
