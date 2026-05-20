// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.SecretChats.Application.UseCases;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Application.Handlers
{
    /// <summary>
    /// Validates a self-destruct timer request for an established secret
    /// chat. The encrypted service-message sender is not available in this
    /// bounded context yet, so the handler fails explicitly instead of
    /// pretending the timer was delivered.
    /// </summary>
    internal sealed class SetSelfDestructTimerHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly IComponentLogger _log;

        public SetSelfDestructTimerHandler(
            ISecretChatRepository repo,
            IMtProtoRpcPort rpc,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _log = new TimestampedLogger(log, "SecretChats.SetSelfDestructTimer");
        }

        public async Task<Result<Unit, SecretChatError>> HandleAsync(SetSelfDestructTimerCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.Unknown("null command"));
            if (cmd.Seconds < 0)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.NotInExpectedState("seconds must be >= 0"));

            SecretSession session = await _repo.FindAsync(cmd.ChatId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ChatNotFound(cmd.ChatId.ToString()));
            if (session.State != SecretSessionState.Established)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.NotInExpectedState(
                    "SetSelfDestructTimer: session not Established (was " + session.State + ")"));

            _log.Warn("self-destruct timer rejected until encrypted service messages are implemented: chatId="
                + cmd.ChatId + " seconds=" + cmd.Seconds);
            return Result<Unit, SecretChatError>.Fail(SecretChatError.ProtocolError(
                "encrypted service messages are required before self-destruct timers can be changed"));
        }
    }
}
