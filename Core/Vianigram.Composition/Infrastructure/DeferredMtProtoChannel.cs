// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// DeferredMtProtoChannel.cs
// Wraps a Task<MtProtoChannel> resolved off the cold-launch critical path so
// the composition root can return without awaiting the DH
// handshake. Calls made before the underlying task completes await it (with
// a bounded timeout); calls made after short-circuit straight to the live
// channel. The composition root passes the in-flight Task<MtProtoChannel>
// (kicked off by Vianigram.MTProto.MtProtoChannel.OpenAsync(...)) into this
// adapter; UI render proceeds in parallel.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Wraps a <see cref="Task{TResult}"/> of <see cref="Vianigram.MTProto.MtProtoChannel"/>
    /// that resolves once DH (or the cached-key reopen) completes. The task may
    /// already be running or may be started lazily on first use.
    /// </summary>
    public sealed class DeferredMtProtoChannel
    {
        private readonly object _startGate = new object();
        private readonly Func<Task<Vianigram.MTProto.MtProtoChannel>> _channelFactory;
        private readonly TimeSpan _maxWait;
        private Task<Vianigram.MTProto.MtProtoChannel> _channelTask;

        /// <summary>
        /// Constructs a deferred wrapper. The underlying task should already
        /// be running (or about to run) — this type does not start it.
        /// </summary>
        /// <param name="channelTask">In-flight task that produces the live
        ///   <see cref="Vianigram.MTProto.MtProtoChannel"/> on success. Must
        ///   not be <c>null</c>.</param>
        /// <param name="maxWait">Upper bound on how long any single
        ///   <see cref="GetAsync"/> call will wait for the task to complete
        ///   before throwing <see cref="OperationCanceledException"/>. The
        ///   caller's cancellation token is honoured independently.</param>
        public DeferredMtProtoChannel(
            Task<Vianigram.MTProto.MtProtoChannel> channelTask,
            TimeSpan maxWait)
        {
            if (channelTask == null) throw new ArgumentNullException("channelTask");
            if (maxWait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("maxWait");
            _channelTask = channelTask;
            _maxWait = maxWait;
        }

        /// <summary>
        /// Constructs a deferred wrapper whose underlying open task is created
        /// only when the channel is first requested.
        /// </summary>
        public DeferredMtProtoChannel(
            Func<Task<Vianigram.MTProto.MtProtoChannel>> channelFactory,
            TimeSpan maxWait)
        {
            if (channelFactory == null) throw new ArgumentNullException("channelFactory");
            if (maxWait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("maxWait");
            _channelFactory = channelFactory;
            _maxWait = maxWait;
        }

        /// <summary>
        /// Returns the live channel, awaiting the in-flight task if needed.
        /// Throws <see cref="OperationCanceledException"/> if the caller's
        /// token cancels or the configured max-wait elapses; throws whatever
        /// the underlying task threw on faulted completion.
        /// </summary>
        public async Task<Vianigram.MTProto.MtProtoChannel> GetAsync(CancellationToken ct)
        {
            Task<Vianigram.MTProto.MtProtoChannel> channelTask = GetTask();

            // Fast path: task already settled.
            if (channelTask.IsCompleted)
            {
                // Surface task exceptions directly; await rethrows if faulted.
                return await channelTask.ConfigureAwait(false);
            }

            // Slow path: race the task against (caller-ct OR max-wait timeout).
            using (var timeoutCts = new CancellationTokenSource(_maxWait))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
            {
                var timeoutTask = Task.Delay(Timeout.Infinite, linkedCts.Token);
                var winner = await Task.WhenAny(channelTask, timeoutTask).ConfigureAwait(false);
                if (winner != channelTask)
                {
                    // Either caller cancelled or our budget expired.
                    throw new OperationCanceledException("DeferredMtProtoChannel: handshake still running");
                }
                return await channelTask.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Starts the deferred open if needed and returns the underlying task
        /// without applying the per-RPC max-wait budget.
        /// </summary>
        public Task<Vianigram.MTProto.MtProtoChannel> GetTask()
        {
            var task = _channelTask;
            if (task != null) return task;

            lock (_startGate)
            {
                task = _channelTask;
                if (task != null) return task;

                try
                {
                    task = _channelFactory();
                    if (task == null)
                    {
                        throw new InvalidOperationException("DeferredMtProtoChannel factory returned null");
                    }
                }
                catch (Exception ex)
                {
                    var tcs = new TaskCompletionSource<Vianigram.MTProto.MtProtoChannel>();
                    tcs.SetException(ex);
                    task = tcs.Task;
                }

                _channelTask = task;
                return task;
            }
        }

        /// <summary>
        /// True if the underlying handshake task has completed (successfully
        /// or otherwise). Useful for diagnostics; callers should still go
        /// through <see cref="GetAsync"/> to surface exceptions.
        /// </summary>
        public bool IsResolved
        {
            get { return _channelTask != null && _channelTask.IsCompleted; }
        }
    }
}
