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
using Vianigram.Messages.Domain.Entities;
using Vianigram.Messages.Domain.Events;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Infrastructure;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Application.Handlers
{
    /// <summary>
    /// Implements the M1-mandatory optimistic-send flow:
    ///
    ///   1. Allocate negative client-temp id.
    ///   2. Build a Message in DeliveryState.Sending.
    ///   3. InsertOptimistic() into the per-peer aggregate.
    ///   4. Publish MessageQueuedForSend on the event bus.
    ///   5. Return the temp id to the caller (synchronously, &lt;50ms target).
    ///   6. In the background, call IMtProtoRpcPort with messages.sendMessage.
    ///   7. On success → ConfirmOptimistic + MessageSent event.
    ///      On failure → MarkFailed + MessageSendFailed event.
    /// </summary>
    public sealed class SendTextMessageHandler
    {
        private readonly IMessageRepository _repo;
        private readonly IMessageIdGenerator _idGen;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public SendTextMessageHandler(
            IMessageRepository repo,
            IMessageIdGenerator idGen,
            IMtProtoRpcPort rpc,
            IEventBus bus,
            IClock clock,
            ILogger log,
            ITelemetry telemetry)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (idGen == null) throw new ArgumentNullException("idGen");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _repo = repo;
            _idGen = idGen;
            _rpc = rpc;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Messages.SendText");
            _telemetry = telemetry;
        }

        /// <summary>
        /// Returns the client-temp id of the optimistically inserted message
        /// once it is visible in the local aggregate and the queued-event has
        /// been published. The actual server round-trip continues in the
        /// background.
        /// </summary>
        public Task<Result<long, MessageError>> HandleAsync(SendTextMessageCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return TaskFromResult(Result<long, MessageError>.Fail(MessageError.InvalidArgument("cmd null")));
            if (!PeerKey.IsValid(cmd.PeerKey))
                return TaskFromResult(Result<long, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey")));
            if (cmd.Text == null)
                return TaskFromResult(Result<long, MessageError>.Fail(MessageError.InvalidArgument("text null")));

            long tempId = _idGen.NextClientTempId();
            var nowUtc = _clock.UtcNow;

            // Build & insert optimistic message — this must be fast.
            var msg = Message.NewOptimistic(cmd.PeerKey, tempId, cmd.Text, cmd.ReplyTo, nowUtc);
            var stream = _repo.GetOrCreateStream(cmd.PeerKey);
            stream.InsertOptimistic(msg);

            // Emit M1 event synchronously before any I/O so the UI can render.
            _bus.Publish(new MessageQueuedForSend(cmd.PeerKey, tempId, msg.Content, nowUtc));
            _telemetry.Track("messages.send.queued", 1);

            // Fire-and-track the network call.
            var unobserved = SendInternalAsync(cmd, tempId, ct);
            // Suppress the "unused variable" warning explicitly.
            GC.KeepAlive(unobserved);

            return TaskFromResult(Result<long, MessageError>.Ok(tempId));
        }

        private async Task SendInternalAsync(SendTextMessageCommand cmd, long tempId, CancellationToken ct)
        {
            try
            {
                long t0 = Environment.TickCount;
                byte[] req = TlEncoder.EncodeSendMessage(cmd.PeerKey, cmd.Text, cmd.ReplyTo, tempId);
                var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
                if (!rpcResult.IsOk)
                {
                    OnFailure(cmd.PeerKey, tempId, rpcResult.Error.ToString());
                    return;
                }

                long serverId;
                DateTime serverDate;
                if (!TlDecoder.TryDecodeSendMessageResponse(rpcResult.Value, out serverId, out serverDate))
                {
                    OnFailure(cmd.PeerKey, tempId, "decode failed");
                    return;
                }

                var stream = _repo.FindStream(cmd.PeerKey);
                if (stream != null)
                {
                    stream.ConfirmOptimistic(tempId, serverId, serverDate);
                    var confirmed = stream.FindByServerId(serverId);
                    if (confirmed != null)
                    {
                        var upsert = await _repo.UpsertMessageAsync(cmd.PeerKey, confirmed, ct).ConfigureAwait(false);
                        if (!upsert.IsOk)
                        {
                            _log.Warn("Messages.SendInternal upsert failed: " + upsert.Error);
                        }
                    }
                }

                _bus.Publish(new MessageSent(cmd.PeerKey, tempId, serverId, serverDate, _clock.UtcNow));
                _telemetry.Track("messages.send.ack_ms", Environment.TickCount - t0, "ms");
            }
            catch (OperationCanceledException)
            {
                OnFailure(cmd.PeerKey, tempId, "cancelled");
            }
            catch (Exception ex)
            {
                _log.Error("SendInternal: " + ex.Message);
                OnFailure(cmd.PeerKey, tempId, ex.Message);
            }
        }

        private void OnFailure(string peerKey, long tempId, string reason)
        {
            var stream = _repo.FindStream(peerKey);
            if (stream != null)
            {
                var pending = stream.FindByClientTempId(tempId);
                if (pending != null) pending.MarkFailed(reason);
            }
            _bus.Publish(new MessageSendFailed(peerKey, tempId, reason, _clock.UtcNow));
            _telemetry.Track("messages.send.failed", 1);
        }

        private static Task<Result<long, MessageError>> TaskFromResult(Result<long, MessageError> value)
        {
            var tcs = new TaskCompletionSource<Result<long, MessageError>>();
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
