// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Notifications.Application.UseCases;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.Entities;
using Vianigram.Notifications.Domain.ValueObjects;
using Vianigram.Notifications.Infrastructure;
using Vianigram.Notifications.Ports.Outbound;

namespace Vianigram.Notifications.Application.Handlers
{
    /// <summary>
    /// Issues <c>account.updateNotifySettings#84be5b93</c> to push the supplied
    /// mute rule to the server, then mirrors the change in the local
    /// <see cref="NotificationProfile"/> aggregate. Domain events are drained
    /// after the persistence write succeeds.
    ///
    /// Errors:
    ///   - Network / cancellation -&gt; <see cref="NotificationsError.NetworkError"/>.
    ///   - Server SETTINGS_INVALID -&gt; <see cref="NotificationsError.NotInExpectedState"/>.
    ///   - Unexpected exceptions -&gt; <see cref="NotificationsError.Unknown"/>.
    /// </summary>
    internal sealed class UpdateMuteRuleHandler
    {
        private readonly INotificationProfileRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;
        private readonly IPeerAccessHashPort _peerHashes;

        public UpdateMuteRuleHandler(
            INotificationProfileRepository repo,
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger log,
            IClock clock,
            IPeerAccessHashPort peerHashes = null)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Notifications.UpdateMuteRule");
            _clock = clock;
            _peerHashes = peerHashes;
        }

        public async Task<Result<Unit, NotificationsError>> HandleAsync(UpdateMuteRuleCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("null command"));
            if (cmd.Rule == null) return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("rule required"));

            string peerKey = string.IsNullOrEmpty(cmd.PeerKey) ? MuteRule.Global : cmd.PeerKey;

            NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);

            long accessHash = ResolveAccessHash(peerKey);
            if (accessHash == 0L && RequiresAccessHash(peerKey))
            {
                _log.Warn("account.updateNotifySettings peer access_hash missing peer=" + peerKey);
            }

            byte[] request = TlEncoder.EncodeUpdateNotifySettings(peerKey, cmd.Rule, accessHash);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("account.updateNotifySettings failed: " + mapped);
                return Result<Unit, NotificationsError>.Fail(mapped);
            }

            DateTime now = _clock.UtcNow;
            profile.SetMute(peerKey, cmd.Rule, now);
            await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(profile, _bus);
            return Result<Unit, NotificationsError>.Ok(Unit.Value);
        }

        private long ResolveAccessHash(string peerKey)
        {
            if (_peerHashes == null || string.IsNullOrEmpty(peerKey)) return 0L;

            string[] parts = peerKey.Split(':');
            if (parts.Length < 2) return 0L;

            long id;
            if (!long.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out id))
                return 0L;

            if (string.Equals(parts[0], "user", StringComparison.Ordinal))
                return _peerHashes.GetUserAccessHash(id);
            if (string.Equals(parts[0], "channel", StringComparison.Ordinal))
                return _peerHashes.GetChannelAccessHash(id);

            return 0L;
        }

        private static bool RequiresAccessHash(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return false;
            return peerKey.StartsWith("user:", StringComparison.Ordinal)
                || peerKey.StartsWith("channel:", StringComparison.Ordinal);
        }
    }
}
