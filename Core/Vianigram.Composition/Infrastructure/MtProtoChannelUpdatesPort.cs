// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Updates port that follows the live MTProto channel owned by
    /// <see cref="MtProtoChannelAdapter"/>. The app can boot anonymously,
    /// then migrate/reopen the main channel after login; subscriptions stay
    /// registered and are rebound to the newest native channel.
    /// </summary>
    public sealed class MtProtoChannelUpdatesPort : IUpdatesPort, IDisposable
    {
        private readonly MtProtoChannelAdapter _channelSource;
        private readonly IPeerCache _peerCache;
        private readonly object _gate = new object();
        private readonly List<Registration> _registrations = new List<Registration>();

        private MtProtoUpdatesAdapter _adapter;
        private Vianigram.MTProto.MtProtoChannel _attachedChannel;
        private bool _disposed;

        public MtProtoChannelUpdatesPort(
            MtProtoChannelAdapter channelSource,
            Vianigram.MTProto.MtProtoChannel initialChannel,
            IPeerCache peerCache)
        {
            if (channelSource == null) throw new ArgumentNullException("channelSource");
            _channelSource = channelSource;
            _peerCache = peerCache;
            _channelSource.ChannelChanged += OnChannelChanged;

            if (initialChannel != null)
            {
                AttachToChannel(initialChannel, "initial-live");
            }
        }

        public IDisposable Subscribe(Func<byte[], Task> handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");

            Registration registration;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("MtProtoChannelUpdatesPort");
                registration = new Registration(this, handler);
                _registrations.Add(registration);

                if (_adapter != null)
                {
                    registration.NativeSubscription = _adapter.Subscribe(handler);
                    EarlyLog.Write("Composition.MtProtoUpdates", "subscriber attached to current channel");
                }
            }

            Vianigram.MTProto.MtProtoChannel snapshot = _channelSource.CurrentChannelSnapshot;
            if (snapshot != null)
            {
                AttachToChannel(snapshot, "subscribe-snapshot");
            }
            else
            {
                EarlyLog.Write("Composition.MtProtoUpdates", "subscriber queued; awaiting main channel");
            }

            return registration;
        }

        private void OnChannelChanged(Vianigram.MTProto.MtProtoChannel channel)
        {
            AttachToChannel(channel, "channel-changed");
        }

        private void AttachToChannel(Vianigram.MTProto.MtProtoChannel channel, string reason)
        {
            if (channel == null) return;

            MtProtoUpdatesAdapter oldAdapter = null;
            int subscriberCount;
            lock (_gate)
            {
                if (_disposed) return;
                if (_adapter != null && object.ReferenceEquals(_attachedChannel, channel))
                {
                    return;
                }

                oldAdapter = _adapter;
                _adapter = new MtProtoUpdatesAdapter(channel, _peerCache);
                _attachedChannel = channel;

                for (int i = 0; i < _registrations.Count; i++)
                {
                    Registration r = _registrations[i];
                    if (!r.IsDisposed)
                    {
                        r.NativeSubscription = _adapter.Subscribe(r.Handler);
                    }
                }
                subscriberCount = _registrations.Count;
            }

            if (oldAdapter != null)
            {
                try { oldAdapter.Dispose(); }
                catch { }
            }

            EarlyLog.Write("Composition.MtProtoUpdates", "updates adapter attached reason="
                + (reason ?? string.Empty)
                + " subscribers=" + subscriberCount);
        }

        private void Unsubscribe(Registration registration)
        {
            if (registration == null) return;

            IDisposable native = null;
            lock (_gate)
            {
                _registrations.Remove(registration);
                native = registration.NativeSubscription;
                registration.NativeSubscription = null;
            }

            if (native != null)
            {
                try { native.Dispose(); }
                catch { }
            }
        }

        public void Dispose()
        {
            MtProtoUpdatesAdapter adapter = null;
            Registration[] registrations;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                registrations = _registrations.ToArray();
                _registrations.Clear();
                adapter = _adapter;
                _adapter = null;
                _attachedChannel = null;
            }

            _channelSource.ChannelChanged -= OnChannelChanged;

            for (int i = 0; i < registrations.Length; i++)
            {
                registrations[i].Dispose();
            }
            if (adapter != null)
            {
                try { adapter.Dispose(); }
                catch { }
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly MtProtoChannelUpdatesPort _owner;
            private int _disposed;

            public Registration(MtProtoChannelUpdatesPort owner, Func<byte[], Task> handler)
            {
                _owner = owner;
                Handler = handler;
            }

            public Func<byte[], Task> Handler { get; private set; }
            public IDisposable NativeSubscription { get; set; }
            public bool IsDisposed { get { return Volatile.Read(ref _disposed) != 0; } }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                _owner.Unsubscribe(this);
            }
        }
    }
}
