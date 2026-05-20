// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;
using Vianigram.Messages.Application.Commands;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.Entities;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Infrastructure;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Application.Handlers
{
    /// <summary>
    /// Pages older history via TL <c>messages.getHistory</c> (constructor
    /// <c>0x4423e6c5</c>). Decodes the response into domain Message entities,
    /// merges them into the per-peer aggregate, and returns the page.
    /// </summary>
    public sealed class LoadHistoryHandler
    {
        private readonly IMessageRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IClock _clock;
        private readonly IPeerAccessHashPort _peerHashes;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public LoadHistoryHandler(IMessageRepository repo, IMtProtoRpcPort rpc, IClock clock, ILogger log, ITelemetry telemetry)
            : this(repo, rpc, clock, log, telemetry, null)
        {
        }

        public LoadHistoryHandler(IMessageRepository repo, IMtProtoRpcPort rpc, IClock clock, ILogger log, ITelemetry telemetry, IPeerAccessHashPort peerHashes)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            _repo = repo;
            _rpc = rpc;
            _clock = clock;
            _peerHashes = peerHashes; // optional; null in legacy compositions
            _log = new TimestampedLogger(log, "Messages.LoadHistory");
            _telemetry = telemetry;
        }

        public async Task<Result<MessagePage, MessageError>> HandleAsync(LoadHistoryCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<MessagePage, MessageError>.Fail(MessageError.InvalidArgument("cmd null"));
            if (!PeerKey.IsValid(cmd.PeerKey))
                return Result<MessagePage, MessageError>.Fail(MessageError.InvalidArgument("invalid peerKey"));
            if (cmd.Limit <= 0)
                return Result<MessagePage, MessageError>.Fail(MessageError.InvalidArgument("limit must be positive"));

            long t0 = Environment.TickCount;

            // Resolve the cached access_hash for the peer. inputPeerUser /
            // inputPeerChannel rejected with PEER_ID_INVALID (rapid 72ms
            // failure) when this is 0 and the peer wasn't already a
            // contact. inputPeerChat doesn't need one — the encoder
            // ignores accessHash for that path.
            long accessHash = 0L;
            PeerKind kindParsed = PeerKind.User;
            long peerIdParsed = 0L;
            bool parseOk = PeerKey.TryParse(cmd.PeerKey, out kindParsed, out peerIdParsed);
            if (parseOk && _peerHashes != null)
            {
                if (kindParsed == PeerKind.User) accessHash = _peerHashes.GetUserAccessHash(peerIdParsed);
                else if (kindParsed == PeerKind.Channel) accessHash = _peerHashes.GetChannelAccessHash(peerIdParsed);
            }

            _log.Info(
                "getHistory begin peerKey=" + cmd.PeerKey +
                " parseOk=" + parseOk +
                " kind=" + kindParsed +
                " id=" + peerIdParsed +
                " accessHash=" + (accessHash == 0L ? "0 (MISSING)" : "0x" + accessHash.ToString("x16")) +
                " offset=" + (cmd.OffsetMsgId.HasValue ? cmd.OffsetMsgId.Value.ToString() : "none") +
                " limit=" + cmd.Limit +
                " hashesPort=" + (_peerHashes == null ? "null" : "wired"));

            byte[] req = TlEncoder.EncodeGetHistory(cmd.PeerKey, cmd.OffsetMsgId, cmd.Limit, accessHash);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (!rpcResult.IsOk)
            {
                _log.Warn(
                    "getHistory rpc failed: code=" + (rpcResult.Error == null ? "null" : rpcResult.Error.Code.ToString()) +
                    " msg=\"" + (rpcResult.Error == null ? "" : rpcResult.Error.Message) + "\"" +
                    " inner=" + (rpcResult.Error == null || rpcResult.Error.Inner == null ? "none" : rpcResult.Error.Inner.ToString()));
                return Result<MessagePage, MessageError>.Fail(rpcResult.Error);
            }

            IList<Message> messages;
            bool hasMore;
            if (!TlDecoder.TryDecodeMessages(cmd.PeerKey, rpcResult.Value, out messages, out hasMore))
            {
                _log.Warn("getHistory decode failed: bodyLen=" + (rpcResult.Value == null ? 0 : rpcResult.Value.Length));
                return Result<MessagePage, MessageError>.Fail(MessageError.ProtocolError("could not decode messages.getHistory response"));
            }

            _log.Info(
                "getHistory decoded: count=" + (messages == null ? 0 : messages.Count) +
                " hasMore=" + hasMore +
                " bodyLen=" + (rpcResult.Value == null ? 0 : rpcResult.Value.Length));

            var stream = _repo.GetOrCreateStream(cmd.PeerKey);
            stream.AppendOlderPage(messages, hasMore);

            var upsert = await _repo.UpsertMessagesAsync(cmd.PeerKey, messages, ct).ConfigureAwait(false);
            if (!upsert.IsOk)
            {
                _log.Warn("LoadHistory upsert: " + upsert.Error);
            }

            _telemetry.Track("messages.history.fetch_ms", Environment.TickCount - t0, "ms");
            _telemetry.Track("messages.history.count", messages.Count);

            var page = new MessagePage(messages, hasMore, stream.OldestKnownMessageId);
            return Result<MessagePage, MessageError>.Ok(page);
        }
    }
}
