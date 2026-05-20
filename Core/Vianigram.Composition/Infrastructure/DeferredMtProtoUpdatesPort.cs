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
    /// IUpdatesPort that waits for a deferred MTProto channel before wiring the
    /// native push adapter. This keeps first paint off the socket-open path
    /// while preserving real-time updates once the channel is ready.
    /// </summary>
    public sealed class DeferredMtProtoUpdatesPort : IUpdatesPort, IDisposable
    {
        private readonly Task<Vianigram.MTProto.MtProtoChannel> _channelTask;
        private readonly DeferredMtProtoChannel _deferredChannel;
        private readonly IPeerCache _peerCache;
        private readonly object _gate = new object();
        private readonly List<Registration> _registrations = new List<Registration>();

        private MtProtoUpdatesAdapter _adapter;
        private bool _attachStarted;
        private bool _disposed;

        public DeferredMtProtoUpdatesPort(
            Task<Vianigram.MTProto.MtProtoChannel> channelTask,
            IPeerCache peerCache)
        {
            if (channelTask == null) throw new ArgumentNullException("channelTask");
            _channelTask = channelTask;
            _peerCache = peerCache;
        }

        public DeferredMtProtoUpdatesPort(
            DeferredMtProtoChannel deferredChannel,
            IPeerCache peerCache)
        {
            if (deferredChannel == null) throw new ArgumentNullException("deferredChannel");
            _deferredChannel = deferredChannel;
            _peerCache = peerCache;
        }

        public IDisposable Subscribe(Func<byte[], Task> handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");

            Registration registration;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException("DeferredMtProtoUpdatesPort");

                registration = new Registration(this, handler);
                _registrations.Add(registration);

                if (_adapter != null)
                {
                    registration.NativeSubscription = _adapter.Subscribe(handler);
                    EarlyLog.Write("Composition.MtProtoUpdates", "deferred subscriber attached to live adapter");
                }
                else
                {
                    StartAttachLocked();
                    EarlyLog.Write("Composition.MtProtoUpdates", "deferred subscriber queued; awaiting channel");
                }
            }

            return registration;
        }

        private void StartAttachLocked()
        {
            if (_attachStarted) return;
            _attachStarted = true;
            Task.Run((Func<Task>)AttachWhenReadyAsync);
        }

        private async Task AttachWhenReadyAsync()
        {
            try
            {
                var channel = _deferredChannel != null
                    ? await _deferredChannel.GetTask().ConfigureAwait(false)
                    : await _channelTask.ConfigureAwait(false);
                if (channel == null)
                {
                    EarlyLog.Write("Composition.MtProtoUpdates", "deferred updates attach skipped: channel null");
                    return;
                }

                var adapter = new MtProtoUpdatesAdapter(channel, _peerCache);
                lock (_gate)
                {
                    if (_disposed)
                    {
                        adapter.Dispose();
                        return;
                    }

                    _adapter = adapter;
                    for (int i = 0; i < _registrations.Count; i++)
                    {
                        Registration r = _registrations[i];
                        if (!r.IsDisposed && r.NativeSubscription == null)
                        {
                            r.NativeSubscription = adapter.Subscribe(r.Handler);
                        }
                    }
                }

                EarlyLog.Write("Composition.MtProtoUpdates", "deferred updates adapter wired");
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "Composition.MtProtoUpdates",
                    "deferred updates attach failed: " + ex.GetType().Name + ": " + ex.Message);
            }
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
                native.Dispose();
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
            }

            for (int i = 0; i < registrations.Length; i++)
            {
                registrations[i].Dispose();
            }
            if (adapter != null)
            {
                adapter.Dispose();
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly DeferredMtProtoUpdatesPort _owner;
            private int _disposed;

            public Registration(DeferredMtProtoUpdatesPort owner, Func<byte[], Task> handler)
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
