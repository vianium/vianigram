// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProtoUpdatesAdapter.cs
//
// Adapter for IUpdatesPort backed by the native Vianigram.Core.MTProto
// MtProtoChannel push-subscription API.
//
// The native channel exposes a single OS-level push subscription
// (Subscribe/Unsubscribe pair, token-keyed). We register exactly once at
// construction time and fan-out to every managed handler subscribed via
// IUpdatesPort.Subscribe. This isolates managed handlers from each other
// and from the native callback thread:
//
//   native receive thread
//     → ChannelStateBox push-fanout (raw TL bytes → MtProtoUpdate^)
//       → OnUpdate (this class, native thread)
//         → snapshot handlers under _gate
//         → for each: await handler(bytes) on the thread-pool
//
// Per-handler exceptions are swallowed and logged to debug output: a
// failing managed subscriber must not stop other subscribers (e.g. Sync's
// ProcessUpdatesHandler) from receiving the next update.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Real <see cref="IUpdatesPort"/> adapter that bridges the native
    /// MtProtoChannel push events to managed subscribers. Owns one native
    /// subscription token for the lifetime of the adapter.
    /// </summary>
    public sealed class MtProtoUpdatesAdapter : IUpdatesPort, IDisposable
    {
        private readonly Vianigram.MTProto.MtProtoChannel _channel;
        private readonly long _subscriptionToken;
        private readonly object _gate = new object();
        private readonly List<Func<byte[], Task>> _handlers = new List<Func<byte[], Task>>();
        // Optional peer-cache hydration. Push payloads carry the same
        // users:Vector<User> / chats:Vector<Chat> slices the typed RPCs
        // do, so we feed them through the adapter's permissive partial
        // decoders before fanning out to managed subscribers.
        private readonly IPeerCache _peerCache;
        private int _disposed; // 0 alive, 1 disposed
        private int _pushLogCount;

        public MtProtoUpdatesAdapter(Vianigram.MTProto.MtProtoChannel channel)
            : this(channel, null)
        {
        }

        public MtProtoUpdatesAdapter(Vianigram.MTProto.MtProtoChannel channel, IPeerCache peerCache)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            _channel = channel;
            _peerCache = peerCache;
            // Wire one native subscription that fans-out to managed handlers.
            // The delegate must outlive the adapter (the channel keeps a ref).
            _subscriptionToken = _channel.Subscribe(new Vianigram.MTProto.MtProtoUpdateHandler(OnUpdate));
        }

        // ---- IUpdatesPort ----

        public IDisposable Subscribe(Func<byte[], Task> handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException("MtProtoUpdatesAdapter");
                }
                _handlers.Add(handler);
            }
            return new Subscription(this, handler);
        }

        // ---- Native push callback (invoked on the native receive thread) ----

        private async void OnUpdate(Vianigram.MTProto.MtProtoUpdate update)
        {
            if (update == null) return;
            if (Volatile.Read(ref _disposed) != 0) return;

            byte[] bytes = update.Bytes;
            if (bytes == null) return;
            int pushLogNo = Interlocked.Increment(ref _pushLogCount);
            if (pushLogNo <= 20)
            {
                int handlerCount;
                lock (_gate) { handlerCount = _handlers.Count; }
                EarlyLog.Write("Composition.MtProtoUpdates", "native push len=" + bytes.Length
                    + " root=0x" + PeekCtor(bytes).ToString("x8")
                    + " handlers=" + handlerCount);
            }

            // Hydrate the peer cache from the push payload before fan-out.
            // We use the same permissive partial decoders that the typed-RPC
            // path uses (MtProtoChannelAdapter.TryExtract*Slice),
            // so any User/Chat record embedded in the Updates forest gets
            // its (id, access_hash) captured. Best-effort only — never lets
            // a hydration error block subscriber dispatch.
            if (_peerCache != null)
            {
                try
                {
                    var users = MtProtoChannelAdapter.TryExtractUsersSlice(bytes);
                    var chats = MtProtoChannelAdapter.TryExtractChatsSlice(bytes);
                    if (users != null && users.Count > 0) _peerCache.UpdateFromUsersSlice(users);
                    if (chats != null && chats.Count > 0) _peerCache.UpdateFromChatsSlice(chats);
                }
                catch (Exception ex)
                {
                    EarlyLog.Write("Composition.MtProtoUpdates", "peer-cache hydrate threw: "
                                    + ex.GetType().Name + ": " + ex.Message);
                }
            }

            // Snapshot — the user may add/remove handlers while we iterate.
            Func<byte[], Task>[] handlers;
            lock (_gate)
            {
                if (_handlers.Count == 0) return;
                handlers = _handlers.ToArray();
            }

            for (int i = 0; i < handlers.Length; i++)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                try
                {
                    Task t = handlers[i](bytes);
                    if (t != null)
                    {
                        await t.ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    // Per-handler isolation. One bad subscriber must not
                    // poison the rest. We don't have an ILogger here (port
                    // contract doesn't carry one), so we route through
                    // EarlyLog which preserves the standard log format.
                    EarlyLog.Write("Composition.MtProtoUpdates", "subscriber threw: "
                                    + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private void Unsubscribe(Func<byte[], Task> handler)
        {
            if (handler == null) return;
            lock (_gate)
            {
                _handlers.Remove(handler);
            }
        }

        private static uint PeekCtor(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return 0;
            return (uint)bytes[0]
                | ((uint)bytes[1] << 8)
                | ((uint)bytes[2] << 16)
                | ((uint)bytes[3] << 24);
        }

        // ---- IDisposable ----

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_gate) { _handlers.Clear(); }
            try { _channel.Unsubscribe(_subscriptionToken); }
            catch (Exception ex)
            {
                EarlyLog.Write("Composition.MtProtoUpdates", "Unsubscribe threw: "
                                + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ---- IDisposable returned by Subscribe() ----

        private sealed class Subscription : IDisposable
        {
            private readonly MtProtoUpdatesAdapter _owner;
            private Func<byte[], Task> _handler;
            public Subscription(MtProtoUpdatesAdapter owner, Func<byte[], Task> handler)
            {
                _owner = owner;
                _handler = handler;
            }
            public void Dispose()
            {
                Func<byte[], Task> h = Interlocked.Exchange(ref _handler, null);
                if (h != null) _owner.Unsubscribe(h);
            }
        }
    }
}
