// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Application;
using Vianigram.Calls.Application.UseCases;
using Vianigram.Calls.Domain.Events;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Sync.Domain.ValueObjects;
using Vianigram.Sync.Infrastructure;
using Vianigram.Sync.Ports.Inbound;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Calls-specific update watchdog. Native push remains the fast path, but
    /// phone calls are too latency-sensitive to rely on a single delivery path:
    /// if the socket push is missed, updates.getDifference is polled briefly
    /// and any embedded updatePhoneCall is routed to CallsApplication.
    /// </summary>
    public sealed class CallsUpdatePoller : IDisposable
    {
        private const uint CtorUpdatePhoneCall = 0xab0f6b1eu;
        private const uint CtorUpdatePhoneCallSignalingData = 0x2661bf09u;

        private const uint CtorPhoneCallEmpty = 0x5366c915u;
        private const uint CtorPhoneCallWaiting = 0xc5226f17u;
        private const uint CtorPhoneCallRequested = 0x14b0ed0cu;
        private const uint CtorPhoneCallAccepted = 0x3660c311u;
        private const uint CtorPhoneCall = 0x30535af5u;
        private const uint CtorPhoneCallLegacy = 0x967f7c67u;
        private const uint CtorPhoneCallDiscarded = 0x50ca4de1u;

        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(4);
        // A backup poller should not be too aggressive when Sync is
        // self-healing (it fires getDifference on every gap detection).
        // 2 s is still <half the typical "ring → answer" window so
        // incoming-call signaling is responsive without burning the radio.
        private static readonly TimeSpan FastPollInterval = TimeSpan.FromSeconds(2);
        // 30 s comfortably covers any realistic call-ring window (Telegram
        // auto-discards unanswered calls around 60 s, but most rings end in
        // 5–15 s). This caps the fast-poll burst at <15 RPCs per call event.
        private static readonly TimeSpan FastWindow = TimeSpan.FromSeconds(30);

        private readonly Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort _rpc;
        private readonly Func<ISyncApi> _resolveSync;
        private readonly Func<CallsApplication> _resolveCalls;
        private readonly IClock _clock;
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly IDisposable[] _subscriptions;
        private readonly object _seenGate = new object();
        private readonly Queue<ulong> _seenOrder = new Queue<ulong>();
        private readonly HashSet<ulong> _seen = new HashSet<ulong>();

        private DateTime _fastUntilUtc;
        private Task _loopTask;
        private int _disposed;
        private int _pollCount;
        private int _failureLogCount;

        public CallsUpdatePoller(
            Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort rpc,
            Func<ISyncApi> resolveSync,
            Func<CallsApplication> resolveCalls,
            IEventBus bus,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (resolveSync == null) throw new ArgumentNullException("resolveSync");
            if (resolveCalls == null) throw new ArgumentNullException("resolveCalls");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");

            _rpc = rpc;
            _resolveSync = resolveSync;
            _resolveCalls = resolveCalls;
            _clock = clock;

            _subscriptions = new IDisposable[]
            {
                // Fast polling is ONLY useful in the narrow "ring" window
                // — between an outgoing CallRequested
                // (peer hasn't answered yet) or an incoming CallReceived
                // (we haven't accepted yet) and the next state transition.
                // Once the call moves into Accepted / Active / Discarded,
                // the signaling channel has done its job — the media
                // plane and Sync's gap-detect getDifference cover the
                // rest. Dropping fast polling on those events caps the
                // burst at ~15 RPCs (FastWindow=30 s, FastInterval=2 s)
                // per call event instead of the previous ~100.
                bus.Subscribe<CallRequested>(delegate { Boost("CallRequested"); }),
                bus.Subscribe<CallReceived>(delegate { Boost("CallReceived"); }),
                bus.Subscribe<CallAccepted>(delegate { DropFast("CallAccepted"); }),
                bus.Subscribe<CallActive>(delegate { DropFast("CallActive"); }),
                bus.Subscribe<CallDiscarded>(delegate { DropFast("CallDiscarded"); })
            };

            _loopTask = Task.Factory.StartNew(
                delegate { return RunAsync(_shutdown.Token); },
                CancellationToken.None,
                TaskCreationOptions.None,
                TaskScheduler.Default).Unwrap();

            EarlyLog.Write("Calls.Poll", "calls update poller started");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _shutdown.Cancel(); } catch { }

            for (int i = 0; i < _subscriptions.Length; i++)
            {
                try { if (_subscriptions[i] != null) _subscriptions[i].Dispose(); }
                catch { }
            }

            try { _shutdown.Dispose(); } catch { }
        }

        private void Boost(string reason)
        {
            _fastUntilUtc = _clock.UtcNow.Add(FastWindow);
            EarlyLog.Write("Calls.Poll", "fast polling enabled reason=" + reason
                + " until=" + _fastUntilUtc.ToString("o"));
        }

        /// <summary>
        /// Pull the fast-poll deadline back to "now" so the next loop
        /// iteration falls into idle cadence (4 s). Used after CallDiscarded
        /// — keeping ~1 Hz polling after the call ends is wasteful.
        /// </summary>
        private void DropFast(string reason)
        {
            DateTime now = _clock.UtcNow;
            if (_fastUntilUtc > now)
            {
                _fastUntilUtc = now.AddSeconds(-1);
                EarlyLog.Write("Calls.Poll", "fast polling dropped reason=" + reason);
            }
        }

        private async Task RunAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await PollOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    LogFailure("poll loop threw " + ex.GetType().Name + ": " + ex.Message);
                }

                TimeSpan delay = IsFastPolling() ? FastPollInterval : IdlePollInterval;
                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task PollOnceAsync(CancellationToken ct)
        {
            ISyncApi sync = null;
            try { sync = _resolveSync(); }
            catch { }

            if (sync == null || !sync.IsCaughtUp) return;

            SyncCursor cursor = sync.CurrentCursor;
            if (cursor == null || cursor.IsInitial) return;

            int pollNo = Interlocked.Increment(ref _pollCount);
            bool fast = IsFastPolling();
            if (fast || pollNo <= 3)
            {
                EarlyLog.Write("Calls.Poll", "updates.getDifference begin poll=" + pollNo
                    + " fast=" + fast
                    + " cursor=" + cursor);
            }

            byte[] req = TlEncoder.EncodeGetDifference(
                cursor.Pts,
                cursor.Date,
                cursor.Qts,
                ptsTotalLimit: 100);

            var rpcResult = await _rpc.InvokeAsync(req, "updates.getDifference(calls)", ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                string msg = rpcResult.Error == null
                    ? "unknown"
                    : (rpcResult.Error.Kind + " code=" + rpcResult.Error.Code + " msg=" + rpcResult.Error.Message);
                LogFailure("updates.getDifference failed: " + msg);
                return;
            }

            byte[] body = rpcResult.Value;
            List<PhoneCallUpdateMatch> matches = FindPhoneCallUpdates(body);
            List<SignalingDataUpdateMatch> signalingMatches = FindSignalingDataUpdates(body);
            if (fast || matches.Count > 0 || signalingMatches.Count > 0 || pollNo <= 3)
            {
                EarlyLog.Write("Calls.Poll", "updates.getDifference ok poll=" + pollNo
                    + " bytes=" + (body == null ? 0 : body.Length)
                    + " root=0x" + PeekCtor(body).ToString("x8")
                    + " phoneUpdates=" + matches.Count
                    + " signalingUpdates=" + signalingMatches.Count);
            }

            int handledOtherUpdates = 0;
            bool dispatchFailed = false;

            for (int i = 0; i < signalingMatches.Count; i++)
            {
                SignalingDataUpdateMatch match = signalingMatches[i];
                ulong hash = Hash(match.Data) ^ ((ulong)match.CallId * 1099511628211UL);
                if (!Remember(hash))
                {
                    if (fast)
                    {
                        EarlyLog.Write("Calls.Poll", "duplicate updatePhoneCallSignalingData skipped hash=0x"
                            + hash.ToString("x16"));
                    }
                    handledOtherUpdates++;
                    continue;
                }

                CallsApplication calls = null;
                try { calls = _resolveCalls(); }
                catch (Exception ex)
                {
                    EarlyLog.Write("Calls.Poll", "resolve CallsApplication failed: "
                        + ex.GetType().Name + ": " + ex.Message);
                }

                if (calls == null)
                {
                    EarlyLog.Write("Calls.Poll", "updatePhoneCallSignalingData skipped: CallsApplication unavailable");
                    dispatchFailed = true;
                    continue;
                }

                EarlyLog.Write("Calls.Poll", "dispatching updatePhoneCallSignalingData offset=" + match.Offset
                    + " callId=" + match.CallId
                    + " data=" + (match.Data == null ? 0 : match.Data.Length) + "B"
                    + " hash=0x" + hash.ToString("x16"));

                var result = await calls
                    .ReceiveSignalingDataAsync(new Vianigram.Calls.Domain.ValueObjects.CallId(match.CallId), match.Data, ct)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    EarlyLog.Write("Calls.Poll", "updatePhoneCallSignalingData dispatch failed: " + result.Error);
                    dispatchFailed = true;
                }
                else
                {
                    EarlyLog.Write("Calls.Poll", "updatePhoneCallSignalingData dispatched");
                    handledOtherUpdates++;
                }
            }

            for (int i = 0; i < matches.Count; i++)
            {
                PhoneCallUpdateMatch match = matches[i];
                ulong hash = Hash(match.PhoneCallTl);
                if (!Remember(hash))
                {
                    if (fast)
                    {
                        EarlyLog.Write("Calls.Poll", "duplicate updatePhoneCall skipped hash=0x"
                            + hash.ToString("x16"));
                    }
                    handledOtherUpdates++;
                    continue;
                }

                CallsApplication calls = null;
                try { calls = _resolveCalls(); }
                catch (Exception ex)
                {
                    EarlyLog.Write("Calls.Poll", "resolve CallsApplication failed: "
                        + ex.GetType().Name + ": " + ex.Message);
                }

                if (calls == null)
                {
                    EarlyLog.Write("Calls.Poll", "updatePhoneCall skipped: CallsApplication unavailable");
                    dispatchFailed = true;
                    continue;
                }

                EarlyLog.Write("Calls.Poll", "dispatching updatePhoneCall offset=" + match.Offset
                    + " phoneCtor=0x" + match.PhoneCtor.ToString("x8")
                    + " hash=0x" + hash.ToString("x16"));

                var result = await calls.UpdateHandler
                    .HandleAsync(new UpdateCallStateCommand(match.PhoneCallTl), ct)
                    .ConfigureAwait(false);

                if (result.IsFail)
                {
                    EarlyLog.Write("Calls.Poll", "updatePhoneCall dispatch failed: " + result.Error);
                    dispatchFailed = true;
                }
                else
                {
                    EarlyLog.Write("Calls.Poll", "updatePhoneCall dispatched");
                    handledOtherUpdates++;
                    if (ShouldBoostAfterPhoneUpdate(match.PhoneCtor))
                    {
                        Boost("updatePhoneCall");
                    }
                    else
                    {
                        DropFast("updatePhoneCall:" + PhoneCtorName(match.PhoneCtor));
                    }
                }
            }

            if (!dispatchFailed)
            {
                var ack = await sync
                    .AcknowledgePolledDifferenceAsync(body, handledOtherUpdates, ct)
                    .ConfigureAwait(false);
                if (ack.IsFail)
                {
                    EarlyLog.Write("Calls.Poll", "updates.getDifference cursor ack failed: " + ack.Error);
                }
            }
            else
            {
                EarlyLog.Write("Calls.Poll", "updates.getDifference cursor ack skipped after dispatch failure");
            }
        }

        private bool IsFastPolling()
        {
            return _clock.UtcNow <= _fastUntilUtc;
        }

        private void LogFailure(string message)
        {
            int n = Interlocked.Increment(ref _failureLogCount);
            if (n <= 10 || IsFastPolling())
            {
                EarlyLog.Write("Calls.Poll", message);
            }
        }

        private bool Remember(ulong hash)
        {
            lock (_seenGate)
            {
                if (_seen.Contains(hash)) return false;
                _seen.Add(hash);
                _seenOrder.Enqueue(hash);
                while (_seenOrder.Count > 128)
                {
                    ulong old = _seenOrder.Dequeue();
                    _seen.Remove(old);
                }
                return true;
            }
        }

        private static List<PhoneCallUpdateMatch> FindPhoneCallUpdates(byte[] bytes)
        {
            var matches = new List<PhoneCallUpdateMatch>();
            if (bytes == null || bytes.Length < 8) return matches;

            for (int offset = 0; offset + 8 <= bytes.Length; offset += 4)
            {
                if (ReadLe32(bytes, offset) != CtorUpdatePhoneCall) continue;

                int phoneCallOffset = offset + 4;
                uint phoneCtor = ReadLe32(bytes, phoneCallOffset);
                if (!LooksLikePhoneCall(phoneCtor)) continue;

                matches.Add(new PhoneCallUpdateMatch(
                    offset,
                    phoneCtor,
                    CopyTail(bytes, phoneCallOffset)));
            }

            return matches;
        }

        private static List<SignalingDataUpdateMatch> FindSignalingDataUpdates(byte[] bytes)
        {
            var matches = new List<SignalingDataUpdateMatch>();
            if (bytes == null || bytes.Length < 16) return matches;
            for (int offset = 0; offset + 16 <= bytes.Length; offset += 4)
            {
                if (ReadLe32(bytes, offset) != CtorUpdatePhoneCallSignalingData) continue;

                long callId;
                byte[] data;
                if (TryReadSignalingData(bytes, offset + 4, out callId, out data))
                {
                    EarlyLog.Write("Calls.Poll", "updatePhoneCallSignalingData observed offset=" + offset
                        + " callId=" + callId
                        + " data=" + (data == null ? 0 : data.Length) + "B");
                    matches.Add(new SignalingDataUpdateMatch(offset, callId, data));
                }
            }
            return matches;
        }

        private static bool LooksLikePhoneCall(uint ctor)
        {
            return ctor == CtorPhoneCallEmpty
                || ctor == CtorPhoneCallWaiting
                || ctor == CtorPhoneCallRequested
                || ctor == CtorPhoneCallAccepted
                || ctor == CtorPhoneCall
                || ctor == CtorPhoneCallLegacy
                || ctor == CtorPhoneCallDiscarded;
        }

        private static bool ShouldBoostAfterPhoneUpdate(uint ctor)
        {
            return ctor == CtorPhoneCallWaiting
                || ctor == CtorPhoneCallRequested;
        }

        private static string PhoneCtorName(uint ctor)
        {
            switch (ctor)
            {
                case CtorPhoneCallWaiting: return "waiting";
                case CtorPhoneCallRequested: return "requested";
                case CtorPhoneCallAccepted: return "accepted";
                case CtorPhoneCall: return "established";
                case CtorPhoneCallLegacy: return "established-legacy";
                case CtorPhoneCallDiscarded: return "discarded";
                case CtorPhoneCallEmpty: return "empty";
                default: return "0x" + ctor.ToString("x8");
            }
        }

        private static byte[] CopyTail(byte[] bytes, int offset)
        {
            int len = bytes.Length - offset;
            byte[] copy = new byte[len];
            Buffer.BlockCopy(bytes, offset, copy, 0, len);
            return copy;
        }

        private static bool TryReadSignalingData(byte[] bytes, int offset, out long callId, out byte[] data)
        {
            callId = 0L;
            data = null;
            if (bytes == null || offset < 0 || offset + 9 > bytes.Length) return false;

            callId = ReadLe64(bytes, offset);
            int p = offset + 8;
            int headerLength;
            int length = ReadTlBytesLength(bytes, p, out headerLength);
            if (length < 0) return false;
            if (p + headerLength + length > bytes.Length) return false;
            data = new byte[length];
            Buffer.BlockCopy(bytes, p + headerLength, data, 0, length);
            return true;
        }

        private static int ReadTlBytesLength(byte[] bytes, int offset, out int headerLength)
        {
            headerLength = 0;
            if (bytes == null || offset < 0 || offset >= bytes.Length) return -1;
            byte first = bytes[offset];
            if (first < 254)
            {
                headerLength = 1;
                return first;
            }
            if (offset + 4 > bytes.Length) return -1;
            headerLength = 4;
            return bytes[offset + 1] | (bytes[offset + 2] << 8) | (bytes[offset + 3] << 16);
        }

        private static uint PeekCtor(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return 0;
            return ReadLe32(bytes, 0);
        }

        private static uint ReadLe32(byte[] bytes, int offset)
        {
            return (uint)bytes[offset]
                | ((uint)bytes[offset + 1] << 8)
                | ((uint)bytes[offset + 2] << 16)
                | ((uint)bytes[offset + 3] << 24);
        }

        private static long ReadLe64(byte[] bytes, int offset)
        {
            uint lo = ReadLe32(bytes, offset);
            uint hi = ReadLe32(bytes, offset + 4);
            return (long)(((ulong)hi << 32) | lo);
        }

        private static ulong Hash(byte[] bytes)
        {
            if (bytes == null) return 0;
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong h = offsetBasis;
            for (int i = 0; i < bytes.Length; i++)
            {
                h ^= bytes[i];
                h *= prime;
            }
            return h;
        }

        private sealed class PhoneCallUpdateMatch
        {
            public PhoneCallUpdateMatch(int offset, uint phoneCtor, byte[] phoneCallTl)
            {
                Offset = offset;
                PhoneCtor = phoneCtor;
                PhoneCallTl = phoneCallTl;
            }

            public int Offset { get; private set; }
            public uint PhoneCtor { get; private set; }
            public byte[] PhoneCallTl { get; private set; }
        }

        private sealed class SignalingDataUpdateMatch
        {
            public SignalingDataUpdateMatch(int offset, long callId, byte[] data)
            {
                Offset = offset;
                CallId = callId;
                Data = data;
            }

            public int Offset { get; private set; }
            public long CallId { get; private set; }
            public byte[] Data { get; private set; }
        }
    }
}
