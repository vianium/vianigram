// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.SecretChats.Application.UseCases;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Infrastructure;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Application.Handlers
{
    /// <summary>
    /// Discard a secret chat — issues <c>messages.discardEncryption</c>,
    /// wipes the local <c>auth_key</c>, transitions the aggregate to
    /// <see cref="SecretSessionState.Discarded"/>, and stages
    /// <see cref="Domain.Events.SecretChatDiscarded"/>.
    ///
    /// <para>Idempotent: invoking discard on an already-discarded session
    /// is a no-op (returns Ok without re-emitting events). The repository
    /// row is retained to allow the UI to keep the conversation entry
    /// visible with a "discarded" marker until the user clears it.</para>
    /// </summary>
    internal sealed class DiscardSecretChatHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public DiscardSecretChatHandler(
            ISecretChatRepository repo,
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "SecretChats.DiscardSecretChat");
            _clock = clock;
        }

        public async Task<Result<Unit, SecretChatError>> HandleAsync(DiscardSecretChatCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SecretChatError>.Fail(SecretChatError.Unknown("null command"));

            SecretSession session = await _repo.FindAsync(cmd.ChatId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ChatNotFound(cmd.ChatId.ToString()));

            if (session.State == SecretSessionState.Discarded)
            {
                // Idempotent — already gone.
                return Result<Unit, SecretChatError>.Ok(Unit.Value);
            }

            byte[] req = TlEncoder.EncodeDiscardEncryption(cmd.ChatId.Value, cmd.DeleteHistory);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.discardEncryption failed: " + mapped);
                // Still wipe locally — discarded-on-server-only is worse than
                // discarded-locally-only from a privacy standpoint. A retry
                // queue to converge the server side is planned.
            }

            DateTime now = _clock.UtcNow;
            session.Discard(cmd.Reason, now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            _log.Info("secret chat discarded: chatId=" + cmd.ChatId + " reason=" + cmd.Reason);
            return Result<Unit, SecretChatError>.Ok(Unit.Value);
        }
    }
}
