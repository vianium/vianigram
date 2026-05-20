// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Privacy.Application.UseCases;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.Entities;
using Vianigram.Privacy.Domain.ValueObjects;
using Vianigram.Privacy.Infrastructure;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Application.Handlers
{
    /// <summary>
    /// Handles <see cref="SetPrivacyRuleCommand"/>: encodes
    /// <c>account.setPrivacy#c9f81ce8</c>, validates the supplied rule
    /// (clauses MUST be non-empty), and records the server's echo of the new
    /// rule on the aggregate.
    /// </summary>
    internal sealed class SetPrivacyRuleHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SetPrivacyRuleHandler(
            IMtProtoRpcPort rpc,
            PrivacyProfile profile,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (profile == null) throw new ArgumentNullException("profile");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _rpc = rpc;
            _profile = profile;
            _bus = bus;
            _log = new TimestampedLogger(log, "Privacy.SetPrivacyRule");
            _clock = clock;
        }

        public async Task<Result<Unit, PrivacyError>> HandleAsync(SetPrivacyRuleCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("null command"));
            if (cmd.Key == PrivacyKey.Unknown)
                return Result<Unit, PrivacyError>.Fail(PrivacyError.InvalidValue("PrivacyKey.Unknown"));
            if (cmd.Rule == null || cmd.Rule.Count == 0)
                return Result<Unit, PrivacyError>.Fail(PrivacyError.InvalidValue("rule must contain at least one clause"));

            byte[] request = TlEncoder.EncodeSetPrivacy(cmd.Key, cmd.Rule);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("account.setPrivacy rpc failed: " + mapped);
                return Result<Unit, PrivacyError>.Fail(mapped);
            }

            try
            {
                // The server echoes the canonical rule list. Re-decode it and
                // record the canonical form (could differ from the input
                // because of server-side normalization).
                PrivacyRule echoed = TlDecoder.DecodePrivacyRules(rpcResult.Value);
                _profile.RecordRule(cmd.Key, echoed, _clock.UtcNow);
                HandlerEventBridge.Drain(_profile, _bus);
                return Result<Unit, PrivacyError>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                _log.Warn("account.setPrivacy decode failed: " + ex.Message);
                // The write succeeded server-side; just record the requested
                // rule locally so the cache is consistent with the user's
                // intent. Future reads will re-fetch and reconcile.
                _profile.RecordRule(cmd.Key, cmd.Rule, _clock.UtcNow);
                HandlerEventBridge.Drain(_profile, _bus);
                return Result<Unit, PrivacyError>.Ok(Unit.Value);
            }
        }
    }
}
