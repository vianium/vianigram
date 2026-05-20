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
    /// Sends an existing scheduled message immediately via TL
    /// <c>messages.sendScheduledMessages</c>.
    /// </summary>
    public sealed class SendScheduledNowHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public SendScheduledNowHandler(IMtProtoRpcPort rpc, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _rpc = rpc;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.SendScheduledNow");
            _telemetry = telemetry;
        }

        public async Task<Result<Unit, MessageError>> HandleAsync(string peerKey, long messageId, CancellationToken ct)
        {
            if (!PeerKey.IsValid(peerKey))
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));
            if (messageId <= 0)
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("messageId must be positive"));

            long t0 = Environment.TickCount;
            var rpcResult = await _rpc.MessagesSendScheduledMessagesAsync(peerKey, messageId, ct).ConfigureAwait(true);
            if (!rpcResult.IsOk)
            {
                _log.Warn("SendScheduledNow failed: " + rpcResult.Error);
                return Result<Unit, MessageError>.Fail(rpcResult.Error);
            }

            _telemetry.Track("messages.sendscheduled.ack_ms", Environment.TickCount - t0, "ms");
            return Result<Unit, MessageError>.Ok(Unit.Value);
        }
    }
}
