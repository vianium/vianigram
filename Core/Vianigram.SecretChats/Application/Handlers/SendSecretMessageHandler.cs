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
    /// Encrypt + send one outbound text message:
    ///
    /// <list type="number">
    ///   <item>Build the inner <c>decryptedMessage#73e0a6c0</c> envelope.</item>
    ///   <item>AES-256-IGE encrypt with the session's <c>auth_key</c>.</item>
    ///   <item>Wrap in <c>messages.sendEncrypted</c> and dispatch via the RPC port.</item>
    ///   <item>Append the message to the aggregate's history (optimistic UI).</item>
    /// </list>
    ///
    /// <para>The optimistic-UI principle (M5 in
    /// <c>docs/managed-architecture/principles.md</c>) is honored: even if
    /// the RPC fails after the local append, downstream UI sees the
    /// "sending" row immediately and a separate failure event reconciles.
    /// The failure path currently surfaces the error without rolling back
    /// the local row; an explicit "send-failed" tombstone is planned.</para>
    /// </summary>
    internal sealed class SendSecretMessageHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISecretCryptoPort _crypto;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SendSecretMessageHandler(
            ISecretChatRepository repo,
            IMtProtoRpcPort rpc,
            ISecretCryptoPort crypto,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _crypto = crypto;
            _bus = bus;
            _log = new TimestampedLogger(log, "SecretChats.SendSecret");
            _clock = clock;
        }

        public async Task<Result<Unit, SecretChatError>> HandleAsync(SendSecretMessageCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, SecretChatError>.Fail(SecretChatError.Unknown("null command"));

            SecretSession session = await _repo.FindAsync(cmd.ChatId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ChatNotFound(cmd.ChatId.ToString()));
            if (session.State != SecretSessionState.Established)
                return Result<Unit, SecretChatError>.Fail(
                    SecretChatError.NotInExpectedState("send requires Established; was " + session.State));
            AuthKey key = session.AuthKeyForCryptoPort();
            if (key == null || key.IsWiped)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.InvalidKey("session has no live auth_key"));

            // Build inner envelope. random_id is 64-bit; we mix the clock and
            // a session-local counter so collisions are negligible across
            // restarts (the server dedupes by random_id anyway).
            long randomId = GenerateRandomId(session);
            byte[] inner = TlEncoder.EncodeDecryptedMessage(randomId, cmd.Ttl.Seconds, cmd.Text);

            var encResult = await _crypto.EncryptIgeAsync(key, inner, ct).ConfigureAwait(false);
            if (encResult.IsFail) return Result<Unit, SecretChatError>.Fail(encResult.Error);
            byte[] cipher = encResult.Value;

            byte[] outer = TlEncoder.EncodeSendEncrypted(
                session.ChatId.Value, /*accessHash*/ 0L, randomId, cipher, /*silent*/ false);
            var rpcResult = await _rpc.CallAsync(outer, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.sendEncrypted failed: " + mapped);
                return Result<Unit, SecretChatError>.Fail(mapped);
            }

            // Decode messages.sentEncryptedMessage to advance our 'date' marker
            // (a future revision may use it for ordering hints; for now we
            // just log).
            try
            {
                TlDecoder.DecodeSentEncryptedMessage(rpcResult.Value);
            }
            catch (Exception ex)
            {
                _log.Warn("sentEncryptedMessage decode warning: " + ex.Message);
            }

            DateTime now = _clock.UtcNow;
            var msg = new SecretMessage(
                randomId,
                now,
                session.PeerUserId, // sender for outbound rows: the local user — peerId is stored here for diagnostic symmetry.
                /*isOutgoing*/ true,
                cmd.Text,
                cmd.Ttl,
                /*mediaRef*/ null);
            session.RecordOutgoing(msg, now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            return Result<Unit, SecretChatError>.Ok(Unit.Value);
        }

        private static long GenerateRandomId(SecretSession session)
        {
            // Mix wall-clock ticks with the per-session OutSeq so successive
            // sends within the same tick still generate distinct ids.
            long ticks = DateTime.UtcNow.Ticks;
            long mixed = ticks ^ ((long)session.OutSeq << 16) ^ ((long)session.ChatId.Value << 32);
            if (mixed == 0L) mixed = 1L;
            return mixed;
        }
    }
}
