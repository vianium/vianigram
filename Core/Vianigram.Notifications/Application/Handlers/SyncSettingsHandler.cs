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
    /// Issues <c>account.getNotifySettings#12b3ad31</c> for the global scopes
    /// (users / chats / broadcasts) and merges each into the local
    /// <see cref="NotificationProfile"/> aggregate. Per-peer exceptions are
    /// fetched separately by other commands; this handler establishes the
    /// global default tree.
    ///
    /// Errors are accumulated: a server failure for one scope does not abort
    /// the remaining requests. The first non-network failure is surfaced as
    /// the result; network errors are returned immediately so the caller can
    /// back off.
    /// </summary>
    internal sealed class SyncSettingsHandler
    {
        private readonly INotificationProfileRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        private static readonly string[] Scopes = new string[]
        {
            "scope:users",
            "scope:chats",
            "scope:broadcasts"
        };

        public SyncSettingsHandler(
            INotificationProfileRepository repo,
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
            _log = new TimestampedLogger(log, "Notifications.SyncSettings");
            _clock = clock;
        }

        public async Task<Result<Unit, NotificationsError>> HandleAsync(SyncSettingsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("null command"));

            NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);
            DateTime now = _clock.UtcNow;

            MuteRule globalRule = null;
            var perScopeOverrides = new List<MuteRule>(Scopes.Length);
            NotificationsError firstError = null;

            for (int i = 0; i < Scopes.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                string scope = Scopes[i];
                byte[] request = TlEncoder.EncodeGetNotifySettings(scope);
                var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
                if (rpcResult.IsFail)
                {
                    var mapped = RpcErrorMapper.Map(rpcResult.Error);
                    _log.Warn("account.getNotifySettings (" + scope + ") failed: " + mapped);
                    if (mapped.Kind == NotificationsErrorKind.NetworkError ||
                        mapped.Kind == NotificationsErrorKind.FloodWait)
                    {
                        return Result<Unit, NotificationsError>.Fail(mapped);
                    }
                    if (firstError == null) firstError = mapped;
                    continue;
                }

                MuteRule rule;
                try
                {
                    rule = TlDecoder.DecodeMuteRule(rpcResult.Value, scope);
                }
                catch (Exception ex)
                {
                    if (firstError == null)
                        firstError = NotificationsError.Unknown("getNotifySettings decode failed for " + scope, ex);
                    continue;
                }

                // First successful scope provides the global default; the
                // others are recorded as scope-keyed overrides.
                if (globalRule == null)
                {
                    globalRule = new MuteRule(MuteRule.Global, rule.MuteUntil, rule.Sound, rule.ShowPreviews);
                }
                perScopeOverrides.Add(rule);
            }

            profile.ApplyServerSync(globalRule, perScopeOverrides, now);
            await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(profile, _bus);

            if (firstError != null && globalRule == null)
            {
                // Every scope failed.
                return Result<Unit, NotificationsError>.Fail(firstError);
            }
            return Result<Unit, NotificationsError>.Ok(Unit.Value);
        }
    }
}
