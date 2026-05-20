// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Infrastructure;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Application.Handlers
{
    /// <summary>
    /// Routes updates surfaced by the <c>Vianigram.Sync</c> context (or by
    /// the smoke harness) into the SecretChats aggregate. Handles the two
    /// inbound update families:
    ///
    /// <list type="bullet">
    ///   <item><c>updateNewEncryptedChat</c> wrapping
    ///         <c>encryptedChatRequested</c> / <c>encryptedChatWaiting</c> /
    ///         <c>encryptedChat</c> / <c>encryptedChatDiscarded</c>.</item>
    ///   <item><c>updateNewEncryptedMessage</c> wrapping the
    ///         <c>encryptedMessage</c> / <c>encryptedMessageService</c> the
    ///         <see cref="ReceiveSecretMessageHandler"/> consumes.</item>
    /// </list>
    ///
    /// <para>Sync delivers raw TL bytes; we decode here and dispatch. The
    /// rationale for keeping update handling in the application layer (vs.
    /// directly inside the orchestrator) is to keep the
    /// <see cref="ISecretChatRepository"/> mutation paths centralized and
    /// transactional with the staged domain events.</para>
    ///
    /// <para>This handler covers the shape transitions driven by
    /// <c>updateEncryption</c>. Initiator-side fingerprint validation
    /// against the server's <c>encryptedChat</c> reply lives here too,
    /// via <see cref="SecretSession.ConfirmWithKey"/>. The crypto
    /// derivation that produces the <see cref="AuthKey"/> argument is
    /// supplied by the caller (orchestrator), since the handler does not
    /// own the in-flight DH key pair.</para>
    /// </summary>
    public sealed class UpdateSecretChatHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public UpdateSecretChatHandler(ISecretChatRepository repo, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _bus = bus;
            _log = new TimestampedLogger(log, "SecretChats.UpdateSecretChat");
            _clock = clock;
        }

        /// <summary>
        /// Apply the body of an <c>updateEncryption</c> /
        /// <c>updateNewEncryptedChat</c> — the raw TL bytes for an
        /// <c>EncryptedChat</c> constructor.
        /// </summary>
        public async Task<Result<Unit, SecretChatError>> ApplyEncryptedChatUpdateAsync(byte[] encryptedChatTlBytes, CancellationToken ct)
        {
            if (encryptedChatTlBytes == null || encryptedChatTlBytes.Length < 4)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ProtocolError("empty EncryptedChat update"));

            TlDecoder.DecodedEncryptedChat decoded;
            try
            {
                decoded = TlDecoder.DecodeEncryptedChat(encryptedChatTlBytes);
            }
            catch (Exception ex)
            {
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ProtocolError("EncryptedChat decode", ex));
            }

            DateTime now = _clock.UtcNow;
            SecretChatId id = new SecretChatId(decoded.ChatId);
            SecretSession session = await _repo.FindAsync(id, ct).ConfigureAwait(false);

            switch (decoded.Shape)
            {
                case TlDecoder.DecodedEncryptedChat.ShapeKind.Empty:
                    _log.Debug("encryptedChatEmpty id=" + decoded.ChatId);
                    return Result<Unit, SecretChatError>.Ok(Unit.Value);

                case TlDecoder.DecodedEncryptedChat.ShapeKind.Waiting:
                    // Initiator-side update confirming server has the request and
                    // is waiting for the peer to accept. If we don't have a
                    // local session yet (cold-restart of the initiator) we
                    // synthesize a Pending row so the UI can render it.
                    if (session == null)
                    {
                        session = SecretSession.StartOutgoing(id, decoded.ParticipantId, now);
                        session.MarkRequestAcknowledged(id, now);
                    }
                    await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                    HandlerEventBridge.Drain(session, _bus);
                    return Result<Unit, SecretChatError>.Ok(Unit.Value);

                case TlDecoder.DecodedEncryptedChat.ShapeKind.Requested:
                    // Responder-side update — peer is asking us to accept. We
                    // must persist the row in Pending state so that
                    // AcceptSecretChatHandler can pick it up; the peer's g_a
                    // is in decoded.DhPublicValue (the orchestrator will stash
                    // it in the application crypto cache).
                    if (session == null)
                    {
                        session = SecretSession.StartIncoming(id, decoded.AdminId, now);
                        await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                        HandlerEventBridge.Drain(session, _bus);
                        _log.Info("encryptedChatRequested id=" + decoded.ChatId + " from=" + decoded.AdminId);
                    }
                    return Result<Unit, SecretChatError>.Ok(Unit.Value);

                case TlDecoder.DecodedEncryptedChat.ShapeKind.Established:
                    // Final shape, both sides. The initiator validates the
                    // peer-asserted key fingerprint here; the responder has
                    // already established via AcceptSecretChatHandler, in
                    // which case this update is a confirmation no-op (we
                    // log and return).
                    if (session == null)
                    {
                        _log.Warn("encryptedChat received for unknown id=" + decoded.ChatId + " — dropping");
                        return Result<Unit, SecretChatError>.Fail(SecretChatError.ChatNotFound(id.ToString()));
                    }
                    if (session.State == SecretSessionState.Established)
                    {
                        // Already confirmed — defensive log but no events.
                        return Result<Unit, SecretChatError>.Ok(Unit.Value);
                    }
                    // The actual ConfirmWithKey call requires the locally-
                    // computed AuthKey, which lives in the orchestrator's
                    // cache (the DH pair from RequestSecretChatHandler).
                    // This is surfaced via the public method below;
                    // the smoke harness drives it explicitly.
                    _log.Info("encryptedChat ready for fingerprint check id=" + decoded.ChatId
                        + " peer_fp=" + decoded.KeyFingerprint.ToString("x16"));
                    return Result<Unit, SecretChatError>.Ok(Unit.Value);

                case TlDecoder.DecodedEncryptedChat.ShapeKind.Discarded:
                    if (session == null)
                    {
                        _log.Debug("discarded update for unknown id=" + decoded.ChatId);
                        return Result<Unit, SecretChatError>.Ok(Unit.Value);
                    }
                    session.Discard(DiscardReason.PeerDiscarded, now);
                    await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                    HandlerEventBridge.Drain(session, _bus);
                    return Result<Unit, SecretChatError>.Ok(Unit.Value);
            }

            return Result<Unit, SecretChatError>.Ok(Unit.Value);
        }

        /// <summary>
        /// Initiator-side fingerprint validation after the peer has accepted.
        /// Caller (orchestrator / smoke harness) supplies the locally-
        /// computed <see cref="AuthKey"/>; this method verifies it matches
        /// the server-asserted fingerprint and transitions the aggregate.
        /// </summary>
        public async Task<Result<Unit, SecretChatError>> ConfirmInitiatorSideAsync(
            SecretChatId chatId,
            AuthKey computedKey,
            KeyFingerprint peerAssertedFingerprint,
            CancellationToken ct)
        {
            if (computedKey == null)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.InvalidKey("computedKey null"));

            SecretSession session = await _repo.FindAsync(chatId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, SecretChatError>.Fail(SecretChatError.ChatNotFound(chatId.ToString()));

            DateTime now = _clock.UtcNow;
            bool ok = session.ConfirmWithKey(computedKey, peerAssertedFingerprint, now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            if (!ok)
                return Result<Unit, SecretChatError>.Fail(
                    SecretChatError.FingerprintMismatch("local_fp=" + computedKey.Fingerprint + " peer_fp=" + peerAssertedFingerprint));
            return Result<Unit, SecretChatError>.Ok(Unit.Value);
        }
    }
}
