// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
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
    /// Schedules a plain-text message via TL <c>messages.sendMessage</c> with
    /// the <c>schedule_date</c> flag. The future-time check is loose (1 second
    /// in the future) since the server clock is authoritative.
    /// </summary>
    public sealed class ScheduleTextHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public ScheduleTextHandler(IMtProtoRpcPort rpc, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _rpc = rpc;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.ScheduleText");
            _telemetry = telemetry;
        }

        public async Task<Result<long, MessageError>> HandleAsync(string peerKey, string text, DateTime sendAtUtc, CancellationToken ct)
        {
            if (!PeerKey.IsValid(peerKey))
                return Result<long, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));
            if (text == null)
                return Result<long, MessageError>.Fail(MessageError.InvalidArgument("text null"));
            if (sendAtUtc.Kind != DateTimeKind.Utc)
                sendAtUtc = DateTime.SpecifyKind(sendAtUtc, DateTimeKind.Utc);
            if (sendAtUtc <= _clock.UtcNow)
                return Result<long, MessageError>.Fail(MessageError.InvalidArgument("sendAtUtc must be in the future"));

            long t0 = Environment.TickCount;
            var rpcResult = await _rpc.MessagesSendScheduledTextAsync(peerKey, text, sendAtUtc, ct).ConfigureAwait(true);
            if (!rpcResult.IsOk)
            {
                _log.Warn("ScheduleText failed: " + rpcResult.Error);
                return Result<long, MessageError>.Fail(rpcResult.Error);
            }

            _telemetry.Track("messages.scheduletext.ack_ms", Environment.TickCount - t0, "ms");
            return Result<long, MessageError>.Ok(rpcResult.Value);
        }
    }
}
