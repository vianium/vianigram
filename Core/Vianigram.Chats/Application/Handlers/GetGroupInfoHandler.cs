// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GetGroupInfoHandler.cs — Vianigram.Chats.Application.Handlers
// Wraps messages.getFullChat / channels.getFullChannel into a typed GroupInfo result.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Returns title / description / member-count / member-list / admin flags for
    /// a chat or channel. Adapter-side dispatches between
    /// <c>messages.getFullChat</c> and <c>channels.getFullChannel</c>; this handler
    /// just maps the resulting <see cref="RawGroupInfo"/> to a domain
    /// <see cref="GroupInfo"/>.
    ///
    /// Read-only: emits no events, touches no repository.
    /// </summary>
    public sealed class GetGroupInfoHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;

        public GetGroupInfoHandler(IMtProtoRpcPort rpc, ILogger log)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Chats.GetGroupInfo");
        }

        public async Task<Result<GroupInfo, ChatError>> HandleAsync(GetGroupInfoCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<GroupInfo, ChatError>.Fail(ChatError.Unknown("null command"));
            if (cmd.Peer.Kind == PeerKind.User)
                return Result<GroupInfo, ChatError>.Fail(ChatError.NotInExpectedState("group info is only meaningful for chat/channel peers"));

            _log.Info("fetching peer=" + cmd.Peer);

            var rpcResult = await _rpc.GetFullPeerAsync(cmd.Peer, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _log.Warn("getFull peer RPC failed: " + rpcResult.Error);
                return Result<GroupInfo, ChatError>.Fail(rpcResult.Error);
            }

            RawGroupInfo raw = rpcResult.Value;
            var members = new List<GroupMember>(raw.Members.Count);
            for (int i = 0; i < raw.Members.Count; i++)
            {
                var m = raw.Members[i];
                members.Add(new GroupMember(m.UserId, m.DisplayName, m.IsAdmin, m.JoinedAt));
            }

            var info = new GroupInfo(
                raw.Peer,
                raw.Title,
                raw.Description,
                raw.MemberCount,
                members,
                raw.IsAdmin,
                raw.IsCreator,
                raw.CreatedAt);

            _log.Info("peer=" + raw.Peer + " members=" + raw.MemberCount);
            return Result<GroupInfo, ChatError>.Ok(info);
        }
    }
}
