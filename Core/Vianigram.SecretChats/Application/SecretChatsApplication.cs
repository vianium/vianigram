// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.SecretChats.Application.Handlers;
using Vianigram.SecretChats.Application.UseCases;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.Events;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Ports.Inbound;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Application
{
    /// <summary>
    /// <see cref="ISecretChatsApi"/> implementation. Dispatches each public
    /// method to the matching handler, surfaces results as
    /// <c>Result&lt;T, SecretChatError&gt;</c>, and re-broadcasts internal
    /// domain events on the kernel bus into two CLR events
    /// (<see cref="SessionChanged"/>, <see cref="MessageReceived"/>) so
    /// XAML/UI consumers don't need an <see cref="IEventBus"/> dependency.
    ///
    /// All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="SecretChatError"/>.
    /// </summary>
    public sealed class SecretChatsApplication : ISecretChatsApi, IDisposable
    {
        private readonly ISecretChatRepository _repo;
        private readonly ISecretCryptoPort _crypto;

        private readonly RequestSecretChatHandler _request;
        private readonly AcceptSecretChatHandler _accept;
        private readonly SendSecretMessageHandler _send;
        private readonly DiscardSecretChatHandler _discard;
        private readonly SetSelfDestructTimerHandler _setTtl;
        private readonly LoadSecretHistoryHandler _loadHistory;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<SecretChatChangedEventArgs> SessionChanged;
        public event EventHandler<SecretMessageReceivedEventArgs> MessageReceived;

        public SecretChatsApplication(
            IMtProtoRpcPort rpc,
            ISecretCryptoPort crypto,
            ISecretChatRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _repo = repo;
            _crypto = crypto;

            _request = new RequestSecretChatHandler(repo, rpc, crypto, bus, logger, clock);
            _accept = new AcceptSecretChatHandler(repo, rpc, crypto, bus, logger, clock);
            _send = new SendSecretMessageHandler(repo, rpc, crypto, bus, logger, clock);
            _discard = new DiscardSecretChatHandler(repo, rpc, bus, logger, clock);
            _setTtl = new SetSelfDestructTimerHandler(repo, rpc, logger, clock);
            _loadHistory = new LoadSecretHistoryHandler(repo, logger);

            _subs = new IDisposable[]
            {
                bus.Subscribe<SecretChatRequested>(OnRequested),
                bus.Subscribe<SecretChatAccepted>(OnAccepted),
                bus.Subscribe<SecretChatEstablished>(OnEstablished),
                bus.Subscribe<SecretChatDiscarded>(OnDiscarded),
                bus.Subscribe<KeyFingerprintMismatch>(OnFingerprintMismatch),
                bus.Subscribe<KeyRekeyed>(OnRekeyed),
                bus.Subscribe<SecretMessageReceived>(OnMessageReceived)
            };
        }

        // ---- ISecretChatsApi -----------------------------------------------

        public async Task<Result<SecretSession, SecretChatError>> RequestAsync(long userId, CancellationToken ct)
        {
            try
            {
                if (userId <= 0)
                    return Result<SecretSession, SecretChatError>.Fail(SecretChatError.NotInExpectedState("userId must be positive"));
                return await _request.HandleAsync(new RequestSecretChatCommand(userId, /*accessHash*/ 0L), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<SecretSession, SecretChatError>.Fail(SecretChatError.Unknown("RequestAsync failed", ex));
            }
        }

        public async Task<Result<SecretSession, SecretChatError>> AcceptAsync(SecretChatId id, CancellationToken ct)
        {
            try
            {
                return await _accept.HandleAsync(new AcceptSecretChatCommand(id), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<SecretSession, SecretChatError>.Fail(SecretChatError.Unknown("AcceptAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SecretChatError>> SendTextAsync(SecretChatId id, string text, CancellationToken ct)
        {
            try
            {
                if (text == null)
                    return Result<Unit, SecretChatError>.Fail(SecretChatError.NotInExpectedState("text required"));
                return await _send.HandleAsync(new SendSecretMessageCommand(id, text), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SecretChatError>.Fail(SecretChatError.Unknown("SendTextAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SecretChatError>> DiscardAsync(SecretChatId id, CancellationToken ct)
        {
            try
            {
                return await _discard.HandleAsync(new DiscardSecretChatCommand(id), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SecretChatError>.Fail(SecretChatError.Unknown("DiscardAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SecretChatError>> SetSelfDestructTimerAsync(SecretChatId id, int seconds, CancellationToken ct)
        {
            try
            {
                return await _setTtl.HandleAsync(new SetSelfDestructTimerCommand(id, seconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SecretChatError>.Fail(SecretChatError.Unknown("SetSelfDestructTimerAsync failed", ex));
            }
        }

        public async Task<Result<SecretMessagePage, SecretChatError>> LoadHistoryAsync(SecretChatId id, long? offsetMsgId, int limit, CancellationToken ct)
        {
            try
            {
                return await _loadHistory.HandleAsync(new LoadSecretHistoryCommand(id, offsetMsgId, limit), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<SecretMessagePage, SecretChatError>.Fail(SecretChatError.Unknown("LoadHistoryAsync failed", ex));
            }
        }

        public SecretSession GetSession(SecretChatId id)
        {
            // Synchronous lookup against the in-memory repository. Other
            // adapters (SQLite) may need to block briefly here; that's
            // acceptable per principles.md §M9 because GetSession is a UI
            // affordance, not a hot-path operation.
            try
            {
                return _repo.FindAsync(id, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }

        public EmojiKey GetEmojiKey(SecretChatId id)
        {
            SecretSession s = GetSession(id);
            if (s == null || !s.HasKey) return null;
            AuthKey key = s.AuthKeyForCryptoPort();
            if (key == null || key.IsWiped) return null;
            return _crypto.RenderEmojiKey(key);
        }

        // ---- bus -> CLR-event bridge ---------------------------------------

        private void OnRequested(SecretChatRequested e)
        {
            RaiseSession(SecretChatChangedEventArgs.ChangeReason.Requested, e.ChatId, e.At);
        }

        private void OnAccepted(SecretChatAccepted e)
        {
            RaiseSession(SecretChatChangedEventArgs.ChangeReason.Accepted, e.ChatId, e.At);
        }

        private void OnEstablished(SecretChatEstablished e)
        {
            RaiseSession(SecretChatChangedEventArgs.ChangeReason.Established, e.ChatId, e.At);
        }

        private void OnDiscarded(SecretChatDiscarded e)
        {
            RaiseSession(SecretChatChangedEventArgs.ChangeReason.Discarded, e.ChatId, e.At);
        }

        private void OnFingerprintMismatch(KeyFingerprintMismatch e)
        {
            RaiseSession(SecretChatChangedEventArgs.ChangeReason.FingerprintMismatch, e.ChatId, e.At);
        }

        private void OnRekeyed(KeyRekeyed e)
        {
            RaiseSession(SecretChatChangedEventArgs.ChangeReason.Rekeyed, e.ChatId, e.At);
        }

        private void OnMessageReceived(SecretMessageReceived e)
        {
            var h = MessageReceived;
            if (h == null) return;
            try
            {
                h(this, new SecretMessageReceivedEventArgs(e.ChatId, e.RandomId, e.At));
            }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        private void RaiseSession(SecretChatChangedEventArgs.ChangeReason reason, SecretChatId id, DateTime at)
        {
            var h = SessionChanged;
            if (h == null) return;
            try
            {
                h(this, new SecretChatChangedEventArgs(reason, id, at));
            }
            catch
            {
                // Swallow downstream subscriber faults.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }
    }
}
