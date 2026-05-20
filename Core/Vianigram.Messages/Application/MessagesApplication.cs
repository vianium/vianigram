// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Application.Commands;
using Vianigram.Messages.Application.Handlers;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.Events;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Ports.Inbound;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Application
{
    /// <summary>
    /// Implements <see cref="IMessagesApi"/> by delegating to the per-command
    /// handlers. Also fans the typed domain events from the bus out to the
    /// coalesced <see cref="IMessagesApi.MessagesChanged"/> event so simple
    /// consumers (UI ViewModels) can subscribe to a single notification stream.
    /// </summary>
    public sealed class MessagesApplication : IMessagesApi, IDisposable
    {
        private readonly SendTextMessageHandler _send;
        private readonly EditTextMessageHandler _edit;
        private readonly DeleteMessageHandler _delete;
        private readonly MarkAsReadHandler _read;
        private readonly LoadHistoryHandler _history;
        private readonly ForwardMessagesHandler _forward;
        private readonly SendPollHandler _sendPoll;
        private readonly ScheduleTextHandler _scheduleText;
        private readonly GetScheduledHandler _getScheduled;
        private readonly SendScheduledNowHandler _sendScheduledNow;
        private readonly DeleteScheduledHandler _deleteScheduled;
        private readonly IMessageRepository _repo;
        private readonly IDisposable[] _subs;

        public event EventHandler<MessagesChangedEventArgs> MessagesChanged;

        public MessagesApplication(
            SendTextMessageHandler send,
            EditTextMessageHandler edit,
            DeleteMessageHandler delete,
            MarkAsReadHandler read,
            LoadHistoryHandler history,
            ForwardMessagesHandler forward,
            SendPollHandler sendPoll,
            ScheduleTextHandler scheduleText,
            GetScheduledHandler getScheduled,
            SendScheduledNowHandler sendScheduledNow,
            DeleteScheduledHandler deleteScheduled,
            IMessageRepository repository,
            IEventBus bus)
        {
            if (send == null) throw new ArgumentNullException("send");
            if (edit == null) throw new ArgumentNullException("edit");
            if (delete == null) throw new ArgumentNullException("delete");
            if (read == null) throw new ArgumentNullException("read");
            if (history == null) throw new ArgumentNullException("history");
            if (forward == null) throw new ArgumentNullException("forward");
            if (sendPoll == null) throw new ArgumentNullException("sendPoll");
            if (scheduleText == null) throw new ArgumentNullException("scheduleText");
            if (getScheduled == null) throw new ArgumentNullException("getScheduled");
            if (sendScheduledNow == null) throw new ArgumentNullException("sendScheduledNow");
            if (deleteScheduled == null) throw new ArgumentNullException("deleteScheduled");
            if (repository == null) throw new ArgumentNullException("repository");
            if (bus == null) throw new ArgumentNullException("bus");

            _send = send;
            _edit = edit;
            _delete = delete;
            _read = read;
            _history = history;
            _forward = forward;
            _sendPoll = sendPoll;
            _scheduleText = scheduleText;
            _getScheduled = getScheduled;
            _sendScheduledNow = sendScheduledNow;
            _deleteScheduled = deleteScheduled;
            _repo = repository;

            _subs = new IDisposable[]
            {
                bus.Subscribe<MessageQueuedForSend>(OnQueued),
                bus.Subscribe<MessageSent>(OnSent),
                bus.Subscribe<MessageSendFailed>(OnFailed),
                bus.Subscribe<MessageReceived>(OnReceived),
                bus.Subscribe<MessageEdited>(OnEditedEvt),
                bus.Subscribe<MessageDeleted>(OnDeletedEvt),
                bus.Subscribe<MessageReadByMe>(OnReadEvt),
                // Peer-side state changes.
                bus.Subscribe<MessagesReadByPeer>(OnPeerReadEvt),
                bus.Subscribe<PeerStatusChanged>(OnPeerStatus),
                bus.Subscribe<PeerTypingChanged>(OnPeerTyping)
            };
        }

        public Task<Result<long, MessageError>> SendTextAsync(string peerKey, string text, long? replyToMsgId, CancellationToken ct)
        {
            return _send.HandleAsync(new SendTextMessageCommand(peerKey, text, replyToMsgId), ct);
        }

        public Task<Result<Unit, MessageError>> EditTextAsync(string peerKey, long messageId, string newText, CancellationToken ct)
        {
            return _edit.HandleAsync(new EditTextMessageCommand(peerKey, messageId, newText), ct);
        }

        public Task<Result<Unit, MessageError>> DeleteAsync(string peerKey, long messageId, bool forBoth, CancellationToken ct)
        {
            return _delete.HandleAsync(new DeleteMessageCommand(peerKey, messageId, forBoth), ct);
        }

        public Task<Result<Unit, MessageError>> MarkAsReadAsync(string peerKey, long upToMessageId, CancellationToken ct)
        {
            return _read.HandleAsync(new MarkAsReadCommand(peerKey, upToMessageId), ct);
        }

        public async Task<Result<MessagePage, MessageError>> GetCachedHistoryAsync(string peerKey, long? offsetMsgId, int limit, CancellationToken ct)
        {
            try
            {
                if (!PeerKey.IsValid(peerKey))
                    return Result<MessagePage, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));
                if (limit <= 0)
                    return Result<MessagePage, MessageError>.Fail(MessageError.InvalidArgument("limit must be positive"));

                Result<IList<Domain.Entities.Message>, MessageError> listed =
                    await _repo.ListMessagesAsync(peerKey, offsetMsgId, limit, ct).ConfigureAwait(false);
                if (listed.IsFail)
                    return Result<MessagePage, MessageError>.Fail(listed.Error);

                var stream = _repo.FindStream(peerKey);
                bool hasMoreOlder = stream != null && stream.HasMoreOlder;
                long? oldest = stream != null ? stream.OldestKnownMessageId : null;
                return Result<MessagePage, MessageError>.Ok(new MessagePage(listed.Value, hasMoreOlder, oldest));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<MessagePage, MessageError>.Fail(MessageError.InvalidState("GetCachedHistoryAsync failed: " + ex.GetType().Name));
            }
        }

        public Task<Result<MessagePage, MessageError>> LoadHistoryAsync(string peerKey, long? offsetMsgId, int limit, CancellationToken ct)
        {
            return _history.HandleAsync(new LoadHistoryCommand(peerKey, offsetMsgId, limit), ct);
        }

        public Task<Result<Unit, MessageError>> ForwardAsync(IList<string> destinationPeerKeys, string sourcePeerKey, IList<long> messageIds, string commentText, CancellationToken ct)
        {
            return _forward.HandleAsync(destinationPeerKeys, sourcePeerKey, messageIds, commentText, ct);
        }

        public Task<Result<long, MessageError>> SendPollAsync(string peerKey, PollSpec poll, CancellationToken ct)
        {
            return _sendPoll.HandleAsync(peerKey, poll, ct);
        }

        public Task<Result<long, MessageError>> ScheduleTextAsync(string peerKey, string text, DateTime sendAtUtc, CancellationToken ct)
        {
            return _scheduleText.HandleAsync(peerKey, text, sendAtUtc, ct);
        }

        public Task<Result<MessagePage, MessageError>> GetScheduledAsync(string peerKey, CancellationToken ct)
        {
            return _getScheduled.HandleAsync(peerKey, ct);
        }

        public Task<Result<Unit, MessageError>> SendScheduledNowAsync(string peerKey, long messageId, CancellationToken ct)
        {
            return _sendScheduledNow.HandleAsync(peerKey, messageId, ct);
        }

        public Task<Result<Unit, MessageError>> DeleteScheduledAsync(string peerKey, long messageId, CancellationToken ct)
        {
            return _deleteScheduled.HandleAsync(peerKey, messageId, ct);
        }

        public void Dispose()
        {
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }

        // ---------- Bus -> EventHandler bridge ----------

        private void OnQueued(MessageQueuedForSend e)
        {
            RaiseRich(
                e.PeerKey, MessagesChangeKind.Queued, null, e.ClientTempId,
                body: ExtractTextBody(e.Content),
                fromUserId: null);
        }
        private void OnSent(MessageSent e) { Raise(e.PeerKey, MessagesChangeKind.Sent, e.ServerId, e.ClientTempId); }
        private void OnFailed(MessageSendFailed e) { Raise(e.PeerKey, MessagesChangeKind.SendFailed, null, e.ClientTempId); }
        private void OnReceived(MessageReceived e)
        {
            // Forward Body / FromUserId so the chat page can append the
            // bubble in place. Both fields are optional — the legacy
            // thin-pipeline ctor leaves them empty and the VM falls back
            // to a partial reload.
            RaiseRich(
                e.PeerKey, MessagesChangeKind.Received, e.MessageId, null,
                body: e.Body,
                fromUserId: e.FromUserId == 0L ? (long?)null : e.FromUserId);
        }
        private void OnEditedEvt(MessageEdited e)
        {
            // Forward Body so ChatPage can rewrite the bubble in place.
            // Empty body → VM falls back to its legacy reload path
            // (matches the Received bridge).
            RaiseRich(
                e.PeerKey, MessagesChangeKind.Edited, e.MessageId, null,
                body: e.Body,
                fromUserId: null);
        }
        private void OnDeletedEvt(MessageDeleted e) { Raise(e.PeerKey, MessagesChangeKind.Deleted, e.MessageId, null); }
        private void OnReadEvt(MessageReadByMe e) { Raise(e.PeerKey, MessagesChangeKind.ReadCursorAdvanced, e.UpToMessageId, null); }

        // Peer-side bridge handlers -------------------------------------------
        private void OnPeerReadEvt(MessagesReadByPeer e)
        {
            Raise(e.PeerKey, MessagesChangeKind.PeerReadOurMessages, e.UpToMessageId, null);
        }

        private void OnPeerStatus(PeerStatusChanged e)
        {
            // PeerKey is synthesised from userId at this layer — group /
            // channel presence is tracked separately. UI subscribers
            // filter by FromUserId when they want presence only for the
            // active 1-on-1 peer.
            string peerKey = "user:" + e.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RaiseRich(
                peerKey, MessagesChangeKind.PeerStatusChanged, null, null,
                body: null,
                fromUserId: e.UserId,
                isOnline: e.IsOnline,
                lastOnlineUtc: e.LastOnlineUtc,
                typingAction: null);
        }

        private void OnPeerTyping(PeerTypingChanged e)
        {
            RaiseRich(
                e.PeerKey, MessagesChangeKind.PeerTypingChanged, null, null,
                body: null,
                fromUserId: e.UserId,
                isOnline: null,
                lastOnlineUtc: null,
                typingAction: e.Action);
        }

        private static string ExtractTextBody(MessageContent content)
        {
            var text = content as MessageContentText;
            return text != null ? text.Body : null;
        }

        private void Raise(string peerKey, MessagesChangeKind kind, long? messageId, long? clientTempId)
        {
            var h = MessagesChanged;
            if (h != null) h(this, new MessagesChangedEventArgs(peerKey, kind, messageId, clientTempId));
        }

        private void RaiseRich(
            string peerKey,
            MessagesChangeKind kind,
            long? messageId,
            long? clientTempId,
            string body,
            long? fromUserId,
            bool? isOnline = null,
            DateTime? lastOnlineUtc = null,
            string typingAction = null)
        {
            var h = MessagesChanged;
            if (h == null) return;
            h(this, new MessagesChangedEventArgs(
                peerKey, kind, messageId, clientTempId,
                body, fromUserId,
                isOnline, lastOnlineUtc,
                typingAction));
        }
    }
}
