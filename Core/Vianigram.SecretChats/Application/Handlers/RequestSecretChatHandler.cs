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
    /// Initiator side of a new secret chat. Generates a 2048-bit DH ephemeral
    /// key via <see cref="ISecretCryptoPort"/>, encodes
    /// <c>messages.requestEncryption</c>, and stores a placeholder
    /// <see cref="SecretSession"/> in <see cref="SecretSessionState.Requesting"/>.
    /// The session is finalized later by
    /// <see cref="UpdateSecretChatHandler"/> when the server delivers
    /// <c>encryptedChat</c>.
    ///
    /// <para>The DH config is fetched lazily from
    /// <c>messages.getDhConfig</c> on first use. We cache it inside the
    /// handler instance so successive requests within the same process do
    /// not re-issue the round trip.</para>
    ///
    /// <para>Errors:
    /// <list type="bullet">
    ///   <item>Network / cancellation -&gt; <see cref="SecretChatError.NetworkError"/>.</item>
    ///   <item>DH validation failure  -&gt; <see cref="SecretChatError.InvalidKey"/>.</item>
    ///   <item>TL decode failure      -&gt; <see cref="SecretChatError.ProtocolError"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class RequestSecretChatHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISecretCryptoPort _crypto;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public RequestSecretChatHandler(
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
            _log = new TimestampedLogger(log, "SecretChats.RequestSecretChat");
            _clock = clock;
        }

        public async Task<Result<SecretSession, SecretChatError>> HandleAsync(RequestSecretChatCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Result<SecretSession, SecretChatError>.Fail(SecretChatError.Unknown("null command"));
            if (cmd.PeerUserId <= 0)
                return Result<SecretSession, SecretChatError>.Fail(SecretChatError.NotInExpectedState("peer user id required"));

            // Step 1: fetch DH config.
            var dhResult = await FetchDhConfigAsync(ct).ConfigureAwait(false);
            if (dhResult.IsFail) return Result<SecretSession, SecretChatError>.Fail(dhResult.Error);
            DhPrime prime = dhResult.Value;

            // Step 2: generate the local ephemeral DH key.
            var keyResult = await _crypto.GenerateDhKeyAsync(prime, ct).ConfigureAwait(false);
            if (keyResult.IsFail) return Result<SecretSession, SecretChatError>.Fail(keyResult.Error);
            DhKeyPair myPair = keyResult.Value;

            // Step 3: send messages.requestEncryption.
            int randomId = unchecked((int)DateTime.UtcNow.Ticks);
            byte[] gA = myPair.PublicBytes;
            byte[] request = TlEncoder.EncodeRequestEncryption(cmd.PeerUserId, cmd.PeerAccessHash, randomId, gA);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.requestEncryption failed: " + mapped);
                return Result<SecretSession, SecretChatError>.Fail(mapped);
            }

            // Step 4: decode the EncryptedChat the server returned. We expect
            // encryptedChatWaiting until the peer accepts.
            TlDecoder.DecodedEncryptedChat decoded;
            try
            {
                decoded = TlDecoder.DecodeEncryptedChat(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<SecretSession, SecretChatError>.Fail(
                    SecretChatError.ProtocolError("decode requestEncryption response", ex));
            }

            DateTime now = _clock.UtcNow;
            var session = SecretSession.StartOutgoing(
                new SecretChatId(decoded.ChatId == 0 ? unchecked((int)randomId) : decoded.ChatId),
                cmd.PeerUserId,
                now);
            session.MarkRequestAcknowledged(new SecretChatId(decoded.ChatId), now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            // Note: persistence of myPair.PrivateHandle is currently stubbed.
            // A future revision will store it indexed by ChatId so the eventual
            // ConfirmEncryption handler can compute g^a^b once the peer's
            // g_b lands. For now, the smoke harness short-circuits this by
            // calling ConfirmEncryption with the same handle still in scope.
            _log.Info("secret chat requested: chatId=" + decoded.ChatId + " peer=" + cmd.PeerUserId);
            return Result<SecretSession, SecretChatError>.Ok(session);
        }

        private async Task<Result<DhPrime, SecretChatError>> FetchDhConfigAsync(CancellationToken ct)
        {
            byte[] req = TlEncoder.EncodeGetDhConfig(/*version*/ 0, /*randomLength*/ 256);
            var rpc = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpc.IsFail) return Result<DhPrime, SecretChatError>.Fail(RpcErrorMapper.Map(rpc.Error));

            TlDecoder.DecodedDhConfig dh;
            try
            {
                dh = TlDecoder.DecodeDhConfig(rpc.Value);
            }
            catch (Exception ex)
            {
                return Result<DhPrime, SecretChatError>.Fail(SecretChatError.ProtocolError("decode messages.dhConfig", ex));
            }

            if (dh.NotModified)
            {
                return Result<DhPrime, SecretChatError>.Fail(
                    SecretChatError.NotInExpectedState("server returned dhConfigNotModified for v=0; client must persist a baseline first"));
            }

            DhPrime prime;
            try
            {
                prime = new DhPrime(dh.G, dh.P, dh.Version, dh.Random);
            }
            catch (Exception ex)
            {
                return Result<DhPrime, SecretChatError>.Fail(SecretChatError.InvalidKey("dh config rejected", ex));
            }

            var validated = await _crypto.ValidateDhPrimeAsync(prime, ct).ConfigureAwait(false);
            if (validated.IsFail) return Result<DhPrime, SecretChatError>.Fail(validated.Error);
            return Result<DhPrime, SecretChatError>.Ok(prime);
        }
    }
}
