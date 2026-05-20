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
    /// Edits an existing message via TL <c>messages.editMessage</c>
    /// (<c>0x48f71778</c>). On success, applies the edit locally and emits a
    /// <see cref="MessageEdited"/> domain event.
    /// </summary>
    public sealed class EditTextMessageHandler
    {
        private readonly IMessageRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public EditTextMessageHandler(IMessageRepository repo, IMtProtoRpcPort rpc, IEventBus bus, IClock clock, ILogger log, ITelemetry telemetry)
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
            _log = new TimestampedLogger(log, "Messages.EditText");
            _telemetry = telemetry;
        }

        public async Task<Result<Unit, MessageError>> HandleAsync(EditTextMessageCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("cmd null"));
            if (!PeerKey.IsValid(cmd.PeerKey))
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));
            if (cmd.MessageId <= 0)
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("messageId must be positive"));
            if (cmd.NewText == null)
                return Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("newText null"));

            long t0 = Environment.TickCount;
            byte[] req = TlEncoder.EncodeEditMessage(cmd.PeerKey, cmd.MessageId, cmd.NewText);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (!rpcResult.IsOk) return Result<Unit, MessageError>.Fail(rpcResult.Error);

            // Apply locally — body swap allowed, but rule M4 keeps the audit
            // trail intact via the MessageEdited domain event below.
            var stream = _repo.FindStream(cmd.PeerKey);
            if (stream != null)
            {
                var editedAt = _clock.UtcNow;
                stream.Apply(new MessageEditEvent(cmd.MessageId, new MessageContentText(cmd.NewText), editedAt));

                var msg = stream.FindByServerId(cmd.MessageId);
                if (msg != null)
                {
                    var upsert = await _repo.UpsertMessageAsync(cmd.PeerKey, msg, ct).ConfigureAwait(false);
                    if (!upsert.IsOk) _log.Warn("Edit upsert: " + upsert.Error);
                }

                _bus.Publish(new MessageEdited(cmd.PeerKey, cmd.MessageId, editedAt, _clock.UtcNow));
            }

            _telemetry.Track("messages.edit.ack_ms", Environment.TickCount - t0, "ms");
            return Result<Unit, MessageError>.Ok(Unit.Value);
        }
    }
}
