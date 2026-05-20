// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.Entities;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Ports.Outbound;

namespace Vianigram.Messages.Infrastructure
{
    /// <summary>
    /// In-memory repository: holds the per-peer aggregate roots in memory.
    /// SQLite/disk persistence can be added via a SqliteMessageRepository
    /// that implements the same port. Operations are guarded by a lock object
    /// rather than a full <c>ConcurrentDictionary</c> because we mutate the
    /// aggregate's internal list as well.
    /// </summary>
    public sealed class InMemoryMessageRepository : IMessageRepository
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, MessageStream> _streams = new Dictionary<string, MessageStream>(StringComparer.Ordinal);

        public MessageStream GetOrCreateStream(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) throw new ArgumentException("peerKey required", "peerKey");
            lock (_lock)
            {
                MessageStream s;
                if (!_streams.TryGetValue(peerKey, out s))
                {
                    s = new MessageStream(peerKey);
                    _streams[peerKey] = s;
                }
                return s;
            }
        }

        public MessageStream FindStream(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return null;
            lock (_lock)
            {
                MessageStream s;
                return _streams.TryGetValue(peerKey, out s) ? s : null;
            }
        }

        public Task<Result<Unit, MessageError>> UpsertMessageAsync(string peerKey, Message message, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(peerKey))
                return TaskFromUnitResult(Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("peerKey")));
            if (message == null)
                return TaskFromUnitResult(Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("message")));

            // The aggregate already holds the message; this method exists so a
            // future durable adapter can flush. For the in-memory variant it is
            // a no-op.
            return TaskFromUnitResult(Result<Unit, MessageError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, MessageError>> UpsertMessagesAsync(string peerKey, IList<Message> messages, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(peerKey))
                return TaskFromUnitResult(Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("peerKey")));
            if (messages == null)
                return TaskFromUnitResult(Result<Unit, MessageError>.Fail(MessageError.InvalidArgument("messages")));

            return TaskFromUnitResult(Result<Unit, MessageError>.Ok(Unit.Value));
        }

        public Task<Result<IList<Message>, MessageError>> ListMessagesAsync(string peerKey, long? offsetMsgId, int limit, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(peerKey))
                return TaskFromListResult(Result<IList<Message>, MessageError>.Fail(MessageError.InvalidArgument("peerKey")));
            if (limit <= 0)
                return TaskFromListResult(Result<IList<Message>, MessageError>.Fail(MessageError.InvalidArgument("limit must be positive")));

            lock (_lock)
            {
                MessageStream s;
                if (!_streams.TryGetValue(peerKey, out s))
                {
                    return TaskFromListResult(Result<IList<Message>, MessageError>.Ok((IList<Message>)new List<Message>()));
                }

                var src = s.Messages;
                var dst = new List<Message>(limit);
                for (int i = 0; i < src.Count && dst.Count < limit; i++)
                {
                    var m = src[i];
                    if (offsetMsgId.HasValue && m.Id.IsConfirmed && m.Id.ServerId >= offsetMsgId.Value) continue;
                    dst.Add(m);
                }
                return TaskFromListResult(Result<IList<Message>, MessageError>.Ok((IList<Message>)dst));
            }
        }

        private static Task<Result<Unit, MessageError>> TaskFromUnitResult(Result<Unit, MessageError> v)
        {
            var tcs = new TaskCompletionSource<Result<Unit, MessageError>>();
            tcs.SetResult(v);
            return tcs.Task;
        }

        private static Task<Result<IList<Message>, MessageError>> TaskFromListResult(Result<IList<Message>, MessageError> v)
        {
            var tcs = new TaskCompletionSource<Result<IList<Message>, MessageError>>();
            tcs.SetResult(v);
            return tcs.Task;
        }
    }
}
