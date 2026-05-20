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
    /// Responder side of a new secret chat. Handles the case where Sync has
    /// delivered an <c>encryptedChatRequested</c> (the peer has issued
    /// <c>messages.requestEncryption</c>); this handler:
    ///
    /// <list type="number">
    ///   <item>Looks up the pending session in the repository.</item>
    ///   <item>Generates our own DH ephemeral via <see cref="ISecretCryptoPort"/>.</item>
    ///   <item>Computes the shared secret <c>g_a^b mod p</c> -&gt; <see cref="AuthKey"/>.</item>
    ///   <item>Sends <c>messages.acceptEncryption</c> with our <c>g_b</c> and
    ///         the freshly-derived key fingerprint.</item>
    ///   <item>Transitions the aggregate to <see cref="SecretSessionState.Established"/>.</item>
    /// </list>
    ///
    /// <para>If <c>messages.acceptEncryption</c> succeeds the server treats
    /// the session as Established; both peers can now exchange
    /// <c>messages.sendEncrypted</c> calls.</para>
    /// </summary>
    internal sealed class AcceptSecretChatHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISecretCryptoPort _crypto;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public AcceptSecretChatHandler(
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
            _log = new TimestampedLogger(log, "SecretChats.AcceptSecretChat");
            _clock = clock;
        }

        public async Task<Result<SecretSession, SecretChatError>> HandleAsync(AcceptSecretChatCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Result<SecretSession, SecretChatError>.Fail(SecretChatError.Unknown("null command"));

            SecretSession session = await _repo.FindAsync(cmd.ChatId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<SecretSession, SecretChatError>.Fail(SecretChatError.ChatNotFound(cmd.ChatId.ToString()));
            if (session.State != SecretSessionState.Pending || session.IsInitiator)
                return Result<SecretSession, SecretChatError>.Fail(
                    SecretChatError.NotInExpectedState("session is not a pending incoming request: " + session.State));

            // Current limitation: the Pending session arrived from
            // UpdateSecretChatHandler with the peer's g_a stashed in
            // application memory keyed by ChatId. We cannot read it back
            // here without that side-channel; for the smoke harness we
            // accept a placeholder and let the stub crypto port produce a
            // dummy auth_key. A future revision will: (a) carry g_a on the
            // session aggregate as a transient field cleared when state
            // advances, (b) fetch the persisted DH config from the repository.
            byte[] peerGA = new byte[256]; // PLACEHOLDER — real bytes wired later.
            DhPrime prime = new DhPrime(/*g*/ 2, new byte[256], /*version*/ 0, new byte[0]);

            var keyResult = await _crypto.GenerateDhKeyAsync(prime, ct).ConfigureAwait(false);
            if (keyResult.IsFail) return Result<SecretSession, SecretChatError>.Fail(keyResult.Error);
            DhKeyPair myPair = keyResult.Value;

            var sharedResult = await _crypto.ComputeSharedSecretAsync(myPair, peerGA, prime, ct).ConfigureAwait(false);
            if (sharedResult.IsFail) return Result<SecretSession, SecretChatError>.Fail(sharedResult.Error);
            AuthKey authKey = sharedResult.Value;

            byte[] req = TlEncoder.EncodeAcceptEncryption(
                cmd.ChatId.Value, /*accessHash*/ 0L, myPair.PublicBytes, authKey.Fingerprint.Value);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.acceptEncryption failed: " + mapped);
                authKey.Wipe();
                return Result<SecretSession, SecretChatError>.Fail(mapped);
            }

            DateTime now = _clock.UtcNow;
            session.AcceptWithKey(authKey, now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            _log.Info("secret chat accepted: chatId=" + cmd.ChatId + " fp=" + authKey.Fingerprint);
            return Result<SecretSession, SecretChatError>.Ok(session);
        }
    }
}
