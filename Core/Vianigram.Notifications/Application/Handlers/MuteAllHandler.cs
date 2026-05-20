// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
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
    /// Mute global plus every per-peer override, syncing each to the server
    /// with <c>account.updateNotifySettings#84be5b93</c>. The first server
    /// failure short-circuits the loop and reports the mapped error; rules
    /// successfully synced before the failure are persisted.
    /// </summary>
    internal sealed class MuteAllHandler
    {
        private readonly INotificationProfileRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;
        private readonly IPeerAccessHashPort _peerHashes;

        public MuteAllHandler(
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
            _log = new TimestampedLogger(log, "Notifications.MuteAll");
            _clock = clock;
            _peerHashes = peerHashes;
        }

        public async Task<Result<Unit, NotificationsError>> HandleAsync(MuteAllCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("null command"));

            NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);
            DateTime now = _clock.UtcNow;
            DateTime until = cmd.MuteUntilUtc ?? DateTime.MaxValue;

            // 1. Apply locally so the staged events fire even if the network
            //    leg fails for some peers — the local state is the user's
            //    intent, and the server will be reconciled on next sync.
            profile.MuteAll(cmd.MuteUntilUtc, now);

            // 2. Build the list of (key, rule) pairs to sync. Snapshot AFTER
            //    the local mutate so we sync the new rules.
            var batch = new List<MuteRule>();
            batch.Add(profile.Global);
            IList<MuteRule> overrides = profile.OverridesSnapshot();
            for (int i = 0; i < overrides.Count; i++) batch.Add(overrides[i]);

            NotificationsError firstError = null;
            for (int i = 0; i < batch.Count; i++)
            {
                MuteRule rule = batch[i];
                if (rule == null) continue;
                ct.ThrowIfCancellationRequested();
                long accessHash = ResolveAccessHash(rule.PeerKey);
                if (accessHash == 0L && RequiresAccessHash(rule.PeerKey))
                {
                    _log.Warn("account.updateNotifySettings (muteAll) peer access_hash missing peer=" + rule.PeerKey);
                }

                byte[] request = TlEncoder.EncodeUpdateNotifySettings(rule.PeerKey, rule, accessHash);
                var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
                if (rpcResult.IsFail)
                {
                    var mapped = RpcErrorMapper.Map(rpcResult.Error);
                    _log.Warn("account.updateNotifySettings (muteAll, " + rule.PeerKey + ") failed: " + mapped);
                    if (firstError == null) firstError = mapped;
                    // FloodWait — surface immediately so the caller can back off.
                    if (mapped.Kind == NotificationsErrorKind.FloodWait) break;
                }
            }

            await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(profile, _bus);

            if (firstError != null)
                return Result<Unit, NotificationsError>.Fail(firstError);

            // until referenced for diagnostic clarity in failure paths.
            if (until == DateTime.MinValue) { /* unreachable */ }
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
