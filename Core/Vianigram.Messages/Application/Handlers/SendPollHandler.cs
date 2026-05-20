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
    /// Sends a poll via TL <c>messages.sendMedia</c> wrapping
    /// <c>inputMediaPoll</c>. Validation of the option count / quiz invariants
    /// lives on <see cref="PollSpec"/> itself.
    /// </summary>
    public sealed class SendPollHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public SendPollHandler(IMtProtoRpcPort rpc, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _rpc = rpc;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.SendPoll");
            _telemetry = telemetry;
        }

        public async Task<Result<long, MessageError>> HandleAsync(string peerKey, PollSpec poll, CancellationToken ct)
        {
            if (!PeerKey.IsValid(peerKey))
                return Result<long, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));
            if (poll == null)
                return Result<long, MessageError>.Fail(MessageError.InvalidArgument("poll null"));
            if (string.IsNullOrEmpty(poll.Question))
                return Result<long, MessageError>.Fail(MessageError.InvalidArgument("poll question empty"));

            long t0 = Environment.TickCount;
            var rpcResult = await _rpc.MessagesSendMediaPollAsync(peerKey, poll, ct).ConfigureAwait(true);
            if (!rpcResult.IsOk)
            {
                _log.Warn("SendPoll failed: " + rpcResult.Error);
                return Result<long, MessageError>.Fail(rpcResult.Error);
            }

            _telemetry.Track("messages.sendpoll.ack_ms", Environment.TickCount - t0, "ms");
            return Result<long, MessageError>.Ok(rpcResult.Value);
        }
    }
}
