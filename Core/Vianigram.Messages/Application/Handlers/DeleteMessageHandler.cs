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
    /// Wraps TL <c>messages.deleteMessages</c> (<c>0xe58e95d2</c>) — the
    /// per-channel variant <c>channels.deleteMessages</c> is selected when
    /// the peer is a channel.
    /// </summary>
    public sealed class DeleteMessageHandler
    {
        private readonly IMessageRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public DeleteMessageHandler(IMessageRepository repo, IMtProtoRpcPort rpc, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.Delete");
            _telemetry = telemetry;
        }

        public async Task<Result<Unit, MessageError>> HandleAsync(DeleteMessageCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("cmd null"));
            if (!PeerKey.IsValid(cmd.PeerKey))
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));
            if (cmd.MessageId <= 0)
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("messageId must be positive"));

            long t0 = Environment.TickCount;
            byte[] req = PeerKey.IsChannel(cmd.PeerKey)
                ? TlEncoder.EncodeChannelsDeleteMessages(cmd.PeerKey, cmd.MessageId)
                : TlEncoder.EncodeMessagesDeleteMessages(cmd.MessageId, cmd.ForBoth);

            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (!rpcResult.IsOk) return Result<Unit, MessageError>.Fail(rpcResult.Error);

            var stream = _repo.FindStream(cmd.PeerKey);
            if (stream != null)
            {
                stream.Apply(new MessageDeleteEvent(cmd.MessageId));
                var msg = stream.FindByServerId(cmd.MessageId);
                if (msg != null)
                {
                    var upsert = await _repo.UpsertMessageAsync(cmd.PeerKey, msg, ct).ConfigureAwait(false);
                    if (!upsert.IsOk) _log.Warn("Delete upsert: " + upsert.Error);
                }
                _bus.Publish(new MessageDeleted(cmd.PeerKey, cmd.MessageId, _clock.UtcNow));
            }

            _telemetry.Track("messages.delete.ack_ms", Environment.TickCount - t0, "ms");
            return Result<Unit, MessageError>.Ok(Unit.Value);
        }
    }
}
