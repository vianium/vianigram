// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Application.Handlers
{
    /// <summary>
    /// Fetches the scheduled-history page for a peer via TL
    /// <c>messages.getScheduledHistory</c>. Pure pass-through — scheduled
    /// messages do not currently merge into the per-peer aggregate.
    /// </summary>
    public sealed class GetScheduledHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public GetScheduledHandler(IMtProtoRpcPort rpc, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _rpc = rpc;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.GetScheduled");
            _telemetry = telemetry;
        }

        public async Task<Result<MessagePage, MessageError>> HandleAsync(string peerKey, CancellationToken ct)
        {
            if (!PeerKey.IsValid(peerKey))
                return Result<MessagePage, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));

            long t0 = Environment.TickCount;
            var rpcResult = await _rpc.MessagesGetScheduledHistoryAsync(peerKey, ct).ConfigureAwait(true);
            if (!rpcResult.IsOk)
            {
                _log.Warn("GetScheduled failed: " + rpcResult.Error);
                return Result<MessagePage, MessageError>.Fail(rpcResult.Error);
            }

            _telemetry.Track("messages.getscheduled.ack_ms", Environment.TickCount - t0, "ms");
            if (rpcResult.Value != null)
                _telemetry.Track("messages.getscheduled.count", rpcResult.Value.Messages.Count);
            return Result<MessagePage, MessageError>.Ok(rpcResult.Value);
        }
    }
}
