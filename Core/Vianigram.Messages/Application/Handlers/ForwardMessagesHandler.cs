// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
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
    /// Wraps TL <c>messages.forwardMessages</c>. Validates the destination /
    /// source peer keys, then delegates to the outbound port which fans the
    /// request out per destination and optionally sends a trailing comment.
    /// </summary>
    public sealed class ForwardMessagesHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public ForwardMessagesHandler(IMtProtoRpcPort rpc, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _rpc = rpc;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.Forward");
            _telemetry = telemetry;
        }

        public async Task<Result<Unit, MessageError>> HandleAsync(IList<string> destinationPeerKeys, string sourcePeerKey, IList<long> messageIds, string commentText, CancellationToken ct)
        {
            if (destinationPeerKeys == null || destinationPeerKeys.Count == 0)
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("destinationPeerKeys empty"));
            if (!PeerKey.IsValid(sourcePeerKey))
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("invalid sourcePeerKey"));
            if (messageIds == null || messageIds.Count == 0)
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("messageIds empty"));
            for (int i = 0; i < destinationPeerKeys.Count; i++)
            {
                if (!PeerKey.IsValid(destinationPeerKeys[i]))
                    return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("invalid destinationPeerKey at index " + i));
            }
            for (int i = 0; i < messageIds.Count; i++)
            {
                if (messageIds[i] <= 0)
                    return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("messageId must be positive"));
            }

            long t0 = Environment.TickCount;
            var rpcResult = await _rpc.MessagesForwardMessagesAsync(sourcePeerKey, messageIds, destinationPeerKeys, commentText, ct).ConfigureAwait(true);
            if (!rpcResult.IsOk)
            {
                _log.Warn("Forward failed: " + rpcResult.Error);
                return Result<Unit, MessageError>.Fail(rpcResult.Error);
            }

            _telemetry.Track("messages.forward.ack_ms", Environment.TickCount - t0, "ms");
            _telemetry.Track("messages.forward.fanout", destinationPeerKeys.Count);
            return Result<Unit, MessageError>.Ok(Unit.Value);
        }
    }
}
