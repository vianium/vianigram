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
    /// Handles <see cref="GetPrivacyRulesCommand"/>: encodes
    /// <c>account.getPrivacy#dadbc950</c>, decodes the
    /// <c>account.privacyRules#50a04e45</c> response, records the result on
    /// the <see cref="PrivacyProfile"/>, and drains domain events.
    /// </summary>
    internal sealed class GetPrivacyRulesHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public GetPrivacyRulesHandler(
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
            _log = new TimestampedLogger(log, "Privacy.GetPrivacyRules");
            _clock = clock;
        }

        public async Task<Result<PrivacyRule, PrivacyError>> HandleAsync(GetPrivacyRulesCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<PrivacyRule, PrivacyError>.Fail(PrivacyError.Unknown("null command"));
            if (cmd.Key == PrivacyKey.Unknown)
                return Result<PrivacyRule, PrivacyError>.Fail(PrivacyError.InvalidValue("PrivacyKey.Unknown"));

            byte[] request = TlEncoder.EncodeGetPrivacy(cmd.Key);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("account.getPrivacy rpc failed: " + mapped);
                return Result<PrivacyRule, PrivacyError>.Fail(mapped);
            }

            try
            {
                PrivacyRule rule = TlDecoder.DecodePrivacyRules(rpcResult.Value);
                _profile.RecordRule(cmd.Key, rule, _clock.UtcNow);
                HandlerEventBridge.Drain(_profile, _bus);
                return Result<PrivacyRule, PrivacyError>.Ok(rule);
            }
            catch (Exception ex)
            {
                _log.Warn("account.getPrivacy decode failed: " + ex.Message);
                return Result<PrivacyRule, PrivacyError>.Fail(PrivacyError.Unknown("getPrivacy decode failed", ex));
            }
        }
    }
}
