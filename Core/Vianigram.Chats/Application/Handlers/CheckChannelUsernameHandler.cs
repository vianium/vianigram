// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CheckChannelUsernameHandler.cs — Vianigram.Chats.Application.Handlers
// Wraps channels.checkUsername — pure availability probe, no side effects.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Checks whether a public channel username is available for reservation by
    /// issuing <c>channels.checkUsername</c>. Read-only — emits no events and
    /// touches no repository.
    /// </summary>
    public sealed class CheckChannelUsernameHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;

        public CheckChannelUsernameHandler(IMtProtoRpcPort rpc, ILogger log)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Chats.CheckChannelUsername");
        }

        public async Task<Result<bool, ChatError>> HandleAsync(CheckChannelUsernameCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<bool, ChatError>.Fail(ChatError.Unknown("null command"));

            _log.Info("checking username='" + cmd.Username + "'");

            var rpcResult = await _rpc.ChannelsCheckUsernameAsync(cmd.Username, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _log.Warn("channels.checkUsername failed: " + rpcResult.Error);
                return Result<bool, ChatError>.Fail(rpcResult.Error);
            }

            _log.Info("username='" + cmd.Username + "' available=" + rpcResult.Value);
            return Result<bool, ChatError>.Ok(rpcResult.Value);
        }
    }
}
