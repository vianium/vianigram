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
    /// Decrypt one inbound <c>encryptedMessage</c> already routed here by
    /// <see cref="UpdateSecretChatHandler"/>. Validates the session is
    /// established, AES-256-IGE-decrypts via <see cref="ISecretCryptoPort"/>,
    /// parses the inner <c>decryptedMessage</c>, appends to history, and
    /// stages <see cref="Domain.Events.SecretMessageReceived"/>.
    /// </summary>
    internal sealed class ReceiveSecretMessageHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly ISecretCryptoPort _crypto;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ReceiveSecretMessageHandler(
            ISecretChatRepository repo,
            ISecretCryptoPort crypto,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _crypto = crypto;
            _bus = bus;
            _log = new TimestampedLogger(log, "SecretChats.ReceiveSecret");
            _clock = clock;
        }

        public async Task<Result<Unit, SecretChatError>> HandleAsync(ReceiveSecretMessageCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SecretChatError>.Fail(SecretChatError.Unknown("null command"));

            SecretSession session = await _repo.FindAsync(cmd.ChatId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ChatNotFound(cmd.ChatId.ToString()));
            if (session.State != SecretSessionState.Established)
                return Result<Unit, SecretChatError>.Fail(
                    SecretChatError.NotInExpectedState("receive requires Established; was " + session.State));
            AuthKey key = session.AuthKeyForCryptoPort();
            if (key == null || key.IsWiped)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.InvalidKey("session has no live auth_key"));

            var decResult = await _crypto.DecryptIgeAsync(key, cmd.EncryptedPayload, ct).ConfigureAwait(false);
            if (decResult.IsFail) return Result<Unit, SecretChatError>.Fail(decResult.Error);

            TlDecoder.DecodedDecryptedMessage inner;
            try
            {
                inner = TlDecoder.DecodeDecryptedMessage(decResult.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ProtocolError("inner decode", ex));
            }

            DateTime now = _clock.UtcNow;
            // Telegram delivers random_id on both the outer encryptedMessage
            // (used for queue dedupe) and the inner envelope. Use the inner
            // value as the source of truth — if they disagree the stack will
            // log it but we still commit using the inner id.
            if (inner.RandomId != cmd.RandomId)
            {
                _log.Warn("encryptedMessage random_id mismatch outer=" + cmd.RandomId + " inner=" + inner.RandomId);
            }

            var msg = new SecretMessage(
                inner.RandomId,
                cmd.ServerDate == DateTime.MinValue ? now : cmd.ServerDate,
                session.PeerUserId,
                /*isOutgoing*/ false,
                inner.Message ?? string.Empty,
                inner.Ttl > 0 ? new Ttl(inner.Ttl) : Ttl.None,
                /*mediaRef*/ null);
            session.RecordIncoming(msg, now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            return Result<Unit, SecretChatError>.Ok(Unit.Value);
        }
    }
}
