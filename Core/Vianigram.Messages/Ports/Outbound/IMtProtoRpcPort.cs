// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Ports.Outbound
{
    /// <summary>
    /// Outbound port to the MTProto data plane. Defined locally inside the
    /// Messages bounded context (anti-corruption boundary; we do not reference
    /// other bounded contexts' MTProto port types directly). The composition
    /// root provides an adapter that bridges to <c>Vianigram.Core.MTProto</c>.
    ///
    /// All payloads are TL-serialized opaque buffers — encoding/decoding lives
    /// in <c>Infrastructure/TlEncoder.cs</c> / <c>TlDecoder.cs</c>.
    /// </summary>
    public interface IMtProtoRpcPort
    {
        /// <summary>
        /// Invoke a TL-serialized request and return the TL-serialized response.
        /// FLOOD_WAIT and other MTProto errors must be mapped to typed
        /// <see cref="MessageError"/> values by the adapter — handlers never see
        /// raw RPC error strings (rule M2).
        /// </summary>
        Task<Result<byte[], MessageError>> CallAsync(byte[] tlRequest, CancellationToken ct);

        /// <summary>
        /// Forwards <paramref name="msgIds"/> from <paramref name="sourcePeerKey"/>
        /// to every peer in <paramref name="destPeerKeys"/> (TL
        /// <c>messages.forwardMessages</c>). The adapter is responsible for
        /// fanning the request out per destination and for issuing the optional
        /// <paramref name="commentText"/> follow-up.
        /// </summary>
        Task<Result<Unit, MessageError>> MessagesForwardMessagesAsync(string sourcePeerKey, IList<long> msgIds, IList<string> destPeerKeys, string commentText, CancellationToken ct);

        /// <summary>
        /// Sends a poll to <paramref name="peerKey"/> via TL
        /// <c>messages.sendMedia</c> wrapping <c>inputMediaPoll</c>. Returns the
        /// resulting server message id.
        /// </summary>
        Task<Result<long, MessageError>> MessagesSendMediaPollAsync(string peerKey, PollSpec poll, CancellationToken ct);

        /// <summary>
        /// Schedules a plain-text message via TL <c>messages.sendMessage</c>
        /// with the <c>schedule_date</c> flag set. Returns the scheduled
        /// message id.
        /// </summary>
        Task<Result<long, MessageError>> MessagesSendScheduledTextAsync(string peerKey, string text, DateTime sendAtUtc, CancellationToken ct);

        /// <summary>
        /// Returns the scheduled-history page for <paramref name="peerKey"/>
        /// via TL <c>messages.getScheduledHistory</c>.
        /// </summary>
        Task<Result<MessagePage, MessageError>> MessagesGetScheduledHistoryAsync(string peerKey, CancellationToken ct);

        /// <summary>
        /// Sends an existing scheduled message immediately via TL
        /// <c>messages.sendScheduledMessages</c>.
        /// </summary>
        Task<Result<Unit, MessageError>> MessagesSendScheduledMessagesAsync(string peerKey, long messageId, CancellationToken ct);

        /// <summary>
        /// Deletes a scheduled message via TL
        /// <c>messages.deleteScheduledMessages</c>.
        /// </summary>
        Task<Result<Unit, MessageError>> MessagesDeleteScheduledMessagesAsync(string peerKey, long messageId, CancellationToken ct);
    }
}
