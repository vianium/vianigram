// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IChatsApi.cs — Vianigram.Chats.Ports.Inbound
// Public, exception-free surface of the Chats bounded context.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Chats bounded context, covering dialog access plus
    /// group/channel/forum management.
    /// Every method is async, takes a <see cref="CancellationToken"/>, and returns
    /// <c>Result&lt;T, ChatError&gt;</c>; no exceptions cross this boundary.
    ///
    /// Consumers: presentation/ViewModels, other contexts via ACL adapters,
    /// composition root for wiring.
    /// </summary>
    public interface IChatsApi
    {
        Task<Result<DialogPage, ChatError>> GetDialogsAsync(int limit, DialogCursor cursor, CancellationToken ct);
        Task<Result<Dialog, ChatError>> GetDialogAsync(PeerId peer, CancellationToken ct);
        Task<Result<Unit, ChatError>> RefreshAsync(CancellationToken ct);
        Task<Result<Unit, ChatError>> PinAsync(PeerId peer, CancellationToken ct);
        Task<Result<Unit, ChatError>> UnpinAsync(PeerId peer, CancellationToken ct);
        Task<Result<Unit, ChatError>> MuteAsync(PeerId peer, TimeSpan? until, CancellationToken ct);

        // ---- Group / channel / forum management ------------------------------------------

        /// <summary>
        /// Creates a basic group with the given title and starting member set.
        /// Wraps <c>messages.createChat</c>. The returned <see cref="Dialog"/> is the
        /// freshly minted aggregate, already upserted in the local repository.
        /// </summary>
        Task<Result<Dialog, ChatError>> CreateGroupAsync(string title, IList<long> userIds, CancellationToken ct);

        /// <summary>
        /// Creates a channel (broadcast or megagroup, public or private) with the
        /// given title, description, and optional public username. Wraps
        /// <c>channels.createChannel</c>; if <paramref name="isPublic"/> is true and
        /// <paramref name="username"/> is non-empty, the username is also reserved
        /// in the same flow.
        /// </summary>
        Task<Result<Dialog, ChatError>> CreateChannelAsync(string title, string description, bool isPublic, string username, CancellationToken ct);

        /// <summary>
        /// Checks whether a public channel username is available for reservation.
        /// Wraps <c>channels.checkUsername</c>; returns true if the username is free.
        /// </summary>
        Task<Result<bool, ChatError>> CheckChannelUsernameAsync(string username, CancellationToken ct);

        /// <summary>
        /// Leaves a chat or channel. Wraps <c>messages.deleteChatUser</c> for basic
        /// groups and <c>channels.leaveChannel</c> for channels/megagroups. The
        /// local dialog is removed from the repository on success.
        /// </summary>
        Task<Result<Unit, ChatError>> LeaveAsync(PeerId peer, CancellationToken ct);

        /// <summary>
        /// Returns title/description/member-count/member-list/admin flags for a
        /// chat or channel. Wraps <c>messages.getFullChat</c> /
        /// <c>channels.getFullChannel</c>.
        /// </summary>
        Task<Result<GroupInfo, ChatError>> GetGroupInfoAsync(PeerId peer, CancellationToken ct);

        /// <summary>
        /// Lists topics inside a forum-enabled channel. Wraps
        /// <c>channels.getForumTopics</c>. Order is server-defined (most-recent first).
        /// </summary>
        Task<Result<IList<ForumTopic>, ChatError>> GetForumTopicsAsync(PeerId channel, CancellationToken ct);

        /// <summary>
        /// Creates a new topic in a forum-enabled channel with the given title and
        /// optional icon emoji. Wraps <c>channels.createForumTopic</c>; the returned
        /// <see cref="ForumTopic"/> reflects the server-assigned id.
        /// </summary>
        Task<Result<ForumTopic, ChatError>> CreateForumTopicAsync(PeerId channel, string title, string iconEmoji, CancellationToken ct);

        /// <summary>
        /// CLR event raised when the dialog catalog changes. Subscribers receive a
        /// <see cref="DialogChangedEventArgs"/> describing what happened. Multicast
        /// from the implementation; thread-safe add/remove.
        /// </summary>
        event EventHandler<DialogChangedEventArgs> DialogChanged;
    }
}
