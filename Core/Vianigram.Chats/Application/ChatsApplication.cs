// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ChatsApplication.cs — Vianigram.Chats.Application
// IChatsApi implementation: dispatches inbound calls to per-method handlers.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Application.Handlers;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.Events;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application
{
    /// <summary>
    /// IChatsApi implementation. Dispatches each public method to the matching
    /// command handler, surfaces results as <c>Result&lt;T, ChatError&gt;</c>, and
    /// re-broadcasts internal domain events on the kernel bus into a CLR event
    /// (<see cref="DialogChanged"/>) so XAML/UI consumers don't need an
    /// <see cref="IEventBus"/> dependency.
    ///
    /// All public methods are exception-free across the boundary: any unexpected
    /// failure is mapped to <see cref="ChatError"/>.
    /// </summary>
    public sealed class ChatsApplication : IChatsApi
    {
        private readonly IDialogRepository _repo;
        private readonly LoadDialogListHandler _load;
        private readonly RefreshDialogListHandler _refresh;
        private readonly PinDialogHandler _pin;
        private readonly UnpinDialogHandler _unpin;
        private readonly MuteDialogHandler _mute;

        private readonly CreateGroupHandler _createGroup;
        private readonly CreateChannelHandler _createChannel;
        private readonly CheckChannelUsernameHandler _checkUsername;
        private readonly LeaveHandler _leave;
        private readonly GetGroupInfoHandler _getGroupInfo;
        private readonly GetForumTopicsHandler _getTopics;
        private readonly CreateForumTopicHandler _createTopic;

        // Holds the IDisposable subscriptions returned by IEventBus.Subscribe so the
        // bridge stays alive for the lifetime of the application object. (Composition
        // root keeps a reference to ChatsApplication, which keeps the subscriptions alive.)
        private readonly IDisposable _subAdded;
        private readonly IDisposable _subUpdated;
        private readonly IDisposable _subRemoved;
        private readonly IDisposable _subSynced;

        public event EventHandler<DialogChangedEventArgs> DialogChanged;

        public ChatsApplication(
            IDialogRepository repo,
            IMtProtoRpcPort rpc,
            IEventBus bus)
            : this(repo, rpc, bus, new DebugLogger())
        {
        }

        public ChatsApplication(
            IDialogRepository repo,
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");

            _repo = repo;
            _load = new LoadDialogListHandler(repo, rpc, bus);
            _refresh = new RefreshDialogListHandler(_load);
            _pin = new PinDialogHandler(repo, rpc, bus);
            _unpin = new UnpinDialogHandler(repo, rpc, bus);
            _mute = new MuteDialogHandler(repo, rpc, bus);

            _createGroup = new CreateGroupHandler(repo, rpc, bus, log);
            _createChannel = new CreateChannelHandler(repo, rpc, bus, log);
            _checkUsername = new CheckChannelUsernameHandler(rpc, log);
            _leave = new LeaveHandler(repo, rpc, bus, log);
            _getGroupInfo = new GetGroupInfoHandler(rpc, log);
            _getTopics = new GetForumTopicsHandler(rpc, log);
            _createTopic = new CreateForumTopicHandler(rpc, log);

            _subAdded = bus.Subscribe<DialogAdded>(OnDialogAdded);
            _subUpdated = bus.Subscribe<DialogUpdated>(OnDialogUpdated);
            _subRemoved = bus.Subscribe<DialogRemoved>(OnDialogRemoved);
            _subSynced = bus.Subscribe<DialogListSynced>(OnDialogListSynced);
        }

        // ---- IChatsApi (V1) -----------------------------------------------------

        public async Task<Result<DialogPage, ChatError>> GetDialogsAsync(int limit, DialogCursor cursor, CancellationToken ct)
        {
            try
            {
                if (limit <= 0)
                    return Result<DialogPage, ChatError>.Fail(ChatError.NotInExpectedState("limit must be positive"));
                return await _load.HandleAsync(new LoadDialogListCommand(limit, cursor ?? DialogCursor.Empty), ct)
                                  .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<DialogPage, ChatError>.Fail(ChatError.Unknown("GetDialogsAsync failed", ex));
            }
        }

        public async Task<Result<Dialog, ChatError>> GetDialogAsync(PeerId peer, CancellationToken ct)
        {
            try
            {
                if (peer == null)
                    return Result<Dialog, ChatError>.Fail(ChatError.NotInExpectedState("peer required"));
                Dialog d = await _repo.GetAsync(peer, ct).ConfigureAwait(false);
                if (d == null)
                    return Result<Dialog, ChatError>.Fail(ChatError.PeerNotFound(peer.ToString()));
                return Result<Dialog, ChatError>.Ok(d);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Dialog, ChatError>.Fail(ChatError.Unknown("GetDialogAsync failed", ex));
            }
        }

        public async Task<Result<Unit, ChatError>> RefreshAsync(CancellationToken ct)
        {
            try
            {
                return await _refresh.HandleAsync(RefreshDialogListCommand.Instance, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ChatError>.Fail(ChatError.Unknown("RefreshAsync failed", ex));
            }
        }

        public async Task<Result<Unit, ChatError>> PinAsync(PeerId peer, CancellationToken ct)
        {
            try
            {
                if (peer == null)
                    return Result<Unit, ChatError>.Fail(ChatError.NotInExpectedState("peer required"));
                return await _pin.HandleAsync(new PinDialogCommand(peer), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ChatError>.Fail(ChatError.Unknown("PinAsync failed", ex));
            }
        }

        public async Task<Result<Unit, ChatError>> UnpinAsync(PeerId peer, CancellationToken ct)
        {
            try
            {
                if (peer == null)
                    return Result<Unit, ChatError>.Fail(ChatError.NotInExpectedState("peer required"));
                return await _unpin.HandleAsync(new UnpinDialogCommand(peer), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ChatError>.Fail(ChatError.Unknown("UnpinAsync failed", ex));
            }
        }

        public async Task<Result<Unit, ChatError>> MuteAsync(PeerId peer, TimeSpan? until, CancellationToken ct)
        {
            try
            {
                if (peer == null)
                    return Result<Unit, ChatError>.Fail(ChatError.NotInExpectedState("peer required"));
                return await _mute.HandleAsync(new MuteDialogCommand(peer, until), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ChatError>.Fail(ChatError.Unknown("MuteAsync failed", ex));
            }
        }

        // ---- IChatsApi ---------------------------------------------------------

        public async Task<Result<Dialog, ChatError>> CreateGroupAsync(string title, IList<long> userIds, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(title))
                    return Result<Dialog, ChatError>.Fail(ChatError.NotInExpectedState("title required"));
                if (userIds == null || userIds.Count == 0)
                    return Result<Dialog, ChatError>.Fail(ChatError.NotInExpectedState("at least one userId required"));
                return await _createGroup.HandleAsync(new CreateGroupCommand(title, userIds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Dialog, ChatError>.Fail(ChatError.Unknown("CreateGroupAsync failed", ex));
            }
        }

        public async Task<Result<Dialog, ChatError>> CreateChannelAsync(string title, string description, bool isPublic, string username, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(title))
                    return Result<Dialog, ChatError>.Fail(ChatError.NotInExpectedState("title required"));
                if (isPublic && string.IsNullOrEmpty(username))
                    return Result<Dialog, ChatError>.Fail(ChatError.NotInExpectedState("public channel requires a username"));
                return await _createChannel.HandleAsync(new CreateChannelCommand(title, description, isPublic, username), ct)
                                           .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Dialog, ChatError>.Fail(ChatError.Unknown("CreateChannelAsync failed", ex));
            }
        }

        public async Task<Result<bool, ChatError>> CheckChannelUsernameAsync(string username, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(username))
                    return Result<bool, ChatError>.Fail(ChatError.NotInExpectedState("username required"));
                return await _checkUsername.HandleAsync(new CheckChannelUsernameCommand(username), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<bool, ChatError>.Fail(ChatError.Unknown("CheckChannelUsernameAsync failed", ex));
            }
        }

        public async Task<Result<Unit, ChatError>> LeaveAsync(PeerId peer, CancellationToken ct)
        {
            try
            {
                if (peer == null)
                    return Result<Unit, ChatError>.Fail(ChatError.NotInExpectedState("peer required"));
                return await _leave.HandleAsync(new LeaveCommand(peer), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ChatError>.Fail(ChatError.Unknown("LeaveAsync failed", ex));
            }
        }

        public async Task<Result<GroupInfo, ChatError>> GetGroupInfoAsync(PeerId peer, CancellationToken ct)
        {
            try
            {
                if (peer == null)
                    return Result<GroupInfo, ChatError>.Fail(ChatError.NotInExpectedState("peer required"));
                return await _getGroupInfo.HandleAsync(new GetGroupInfoCommand(peer), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<GroupInfo, ChatError>.Fail(ChatError.Unknown("GetGroupInfoAsync failed", ex));
            }
        }

        public async Task<Result<IList<ForumTopic>, ChatError>> GetForumTopicsAsync(PeerId channel, CancellationToken ct)
        {
            try
            {
                if (channel == null)
                    return Result<IList<ForumTopic>, ChatError>.Fail(ChatError.NotInExpectedState("channel required"));
                if (channel.Kind != PeerKind.Channel)
                    return Result<IList<ForumTopic>, ChatError>.Fail(ChatError.NotInExpectedState("forum topics live under a channel peer"));
                return await _getTopics.HandleAsync(new GetForumTopicsCommand(channel), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<ForumTopic>, ChatError>.Fail(ChatError.Unknown("GetForumTopicsAsync failed", ex));
            }
        }

        public async Task<Result<ForumTopic, ChatError>> CreateForumTopicAsync(PeerId channel, string title, string iconEmoji, CancellationToken ct)
        {
            try
            {
                if (channel == null)
                    return Result<ForumTopic, ChatError>.Fail(ChatError.NotInExpectedState("channel required"));
                if (channel.Kind != PeerKind.Channel)
                    return Result<ForumTopic, ChatError>.Fail(ChatError.NotInExpectedState("forum topics live under a channel peer"));
                if (string.IsNullOrEmpty(title))
                    return Result<ForumTopic, ChatError>.Fail(ChatError.NotInExpectedState("title required"));
                return await _createTopic.HandleAsync(new CreateForumTopicCommand(channel, title, iconEmoji), ct)
                                         .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<ForumTopic, ChatError>.Fail(ChatError.Unknown("CreateForumTopicAsync failed", ex));
            }
        }

        // ---- Bus -> CLR event bridge --------------------------------------------

        private void OnDialogAdded(DialogAdded e)
        {
            Raise(new DialogChangedEventArgs(DialogChangedEventArgs.ChangeReason.Added, e.Peer, null, e.At));
        }

        private void OnDialogUpdated(DialogUpdated e)
        {
            Raise(new DialogChangedEventArgs(DialogChangedEventArgs.ChangeReason.Updated, e.Peer, e.Change, e.At));
        }

        private void OnDialogRemoved(DialogRemoved e)
        {
            Raise(new DialogChangedEventArgs(DialogChangedEventArgs.ChangeReason.Removed, e.Peer, null, e.At));
        }

        private void OnDialogListSynced(DialogListSynced e)
        {
            Raise(new DialogChangedEventArgs(DialogChangedEventArgs.ChangeReason.ListSynced, null, null, e.At));
        }

        private void Raise(DialogChangedEventArgs args)
        {
            var h = DialogChanged;
            if (h != null) h(this, args);
        }

        // Helper used by tests / future composition extensions; not part of IChatsApi.
        internal IList<IDisposable> Subscriptions
        {
            get { return new IDisposable[] { _subAdded, _subUpdated, _subRemoved, _subSynced }; }
        }
    }
}
