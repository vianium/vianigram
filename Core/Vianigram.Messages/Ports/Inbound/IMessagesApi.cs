// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Messages bounded context (V1). All operations
    /// return <see cref="Result{T,TError}"/> — exceptions never cross this
    /// boundary. <see cref="SendTextAsync"/> in particular returns within
    /// the M1 budget (&lt;50ms): the server round-trip happens asynchronously
    /// after the optimistic insert + queued event have been emitted.
    /// </summary>
    public interface IMessagesApi
    {
        /// <summary>
        /// Optimistic send. Returns immediately with the negative client-temp
        /// id of the inserted bubble. Network errors surface later as a
        /// <c>MessageSendFailed</c> domain event keyed by the same temp id.
        /// </summary>
        Task<Result<long, MessageError>> SendTextAsync(string peerKey, string text, long? replyToMsgId, CancellationToken ct);

        Task<Result<Unit, MessageError>> EditTextAsync(string peerKey, long messageId, string newText, CancellationToken ct);

        Task<Result<Unit, MessageError>> DeleteAsync(string peerKey, long messageId, bool forBoth, CancellationToken ct);

        Task<Result<Unit, MessageError>> MarkAsReadAsync(string peerKey, long upToMessageId, CancellationToken ct);

        /// <summary>
        /// Returns the most recent messages already present in the local
        /// repository without issuing a network request. Used by chat surfaces
        /// to paint immediately, then reconcile with <see cref="LoadHistoryAsync"/>
        /// in the background.
        /// </summary>
        Task<Result<MessagePage, MessageError>> GetCachedHistoryAsync(string peerKey, long? offsetMsgId, int limit, CancellationToken ct);

        Task<Result<MessagePage, MessageError>> LoadHistoryAsync(string peerKey, long? offsetMsgId, int limit, CancellationToken ct);

        /// <summary>
        /// Forwards one or more messages from <paramref name="sourcePeerKey"/>
        /// to every peer in <paramref name="destinationPeerKeys"/> (TL
        /// <c>messages.forwardMessages</c>). An optional <paramref name="commentText"/>
        /// is sent as a separate message to each destination after the forward.
        /// </summary>
        Task<Result<Unit, MessageError>> ForwardAsync(IList<string> destinationPeerKeys, string sourcePeerKey, IList<long> messageIds, string commentText, CancellationToken ct);

        /// <summary>
        /// Sends a poll to <paramref name="peerKey"/> via TL
        /// <c>messages.sendMedia</c> wrapping <c>inputMediaPoll</c>. Returns the
        /// server message id of the resulting bubble.
        /// </summary>
        Task<Result<long, MessageError>> SendPollAsync(string peerKey, PollSpec poll, CancellationToken ct);

        /// <summary>
        /// Schedules a plain-text message for delivery at <paramref name="sendAtUtc"/>
        /// via TL <c>messages.sendMessage</c> with <c>schedule_date</c>. Returns
        /// the scheduled message id (positive scheduled-side id).
        /// </summary>
        Task<Result<long, MessageError>> ScheduleTextAsync(string peerKey, string text, DateTime sendAtUtc, CancellationToken ct);

        /// <summary>
        /// Returns the current scheduled-history page for <paramref name="peerKey"/>
        /// via TL <c>messages.getScheduledHistory</c>.
        /// </summary>
        Task<Result<MessagePage, MessageError>> GetScheduledAsync(string peerKey, CancellationToken ct);

        /// <summary>
        /// Sends an existing scheduled message immediately via TL
        /// <c>messages.sendScheduledMessages</c>.
        /// </summary>
        Task<Result<Unit, MessageError>> SendScheduledNowAsync(string peerKey, long messageId, CancellationToken ct);

        /// <summary>
        /// Deletes a scheduled message via TL
        /// <c>messages.deleteScheduledMessages</c>.
        /// </summary>
        Task<Result<Unit, MessageError>> DeleteScheduledAsync(string peerKey, long messageId, CancellationToken ct);

        /// <summary>
        /// Coalesced UI-side notification stream. Listeners receive a single
        /// event per stream change; payload-level data is consumed via the
        /// repository or via subscribing to the typed events on the
        /// IEventBus directly.
        /// </summary>
        event EventHandler<MessagesChangedEventArgs> MessagesChanged;
    }
}
