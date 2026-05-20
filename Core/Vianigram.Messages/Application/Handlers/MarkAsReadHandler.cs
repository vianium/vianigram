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
using Vianigram.Messages.Application.Commands;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.Events;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Infrastructure;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Application.Handlers
{
    /// <summary>
    /// Wraps either <c>messages.readHistory</c> (<c>0x0e306d3a</c>) for
    /// users/basic chats or <c>channels.readHistory</c> (<c>0xcc104937</c>) for
    /// channels. Choice is made on the peerKey prefix to keep the call site
    /// explicit and avoid the wrong-DC RPC.
    /// </summary>
    public sealed class MarkAsReadHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public MarkAsReadHandler(IMtProtoRpcPort rpc, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _rpc = rpc;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.MarkAsRead");
            _telemetry = telemetry;
        }

        public async Task<Result<Unit, MessageError>> HandleAsync(MarkAsReadCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("cmd null"));
            if (!PeerKey.IsValid(cmd.PeerKey))
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));

            long t0 = Environment.TickCount;
            byte[] req = PeerKey.IsChannel(cmd.PeerKey)
                ? TlEncoder.EncodeChannelsReadHistory(cmd.PeerKey, cmd.UpToMessageId)
                : TlEncoder.EncodeMessagesReadHistory(cmd.PeerKey, cmd.UpToMessageId);

            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (!rpcResult.IsOk) return Result<Unit, MessageError>.Fail(rpcResult.Error);

            _bus.Publish(new MessageReadByMe(cmd.PeerKey, cmd.UpToMessageId, _clock.UtcNow));
            _telemetry.Track("messages.read.ack_ms", Environment.TickCount - t0, "ms");
            return Result<Unit, MessageError>.Ok(Unit.Value);
        }
    }
}
