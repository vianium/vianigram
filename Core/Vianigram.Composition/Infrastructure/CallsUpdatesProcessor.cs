// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CallsUpdatesProcessor.cs - bridges updatePhoneCall pushes into Vianigram.Calls.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Application;
using Vianigram.Calls.Application.UseCases;
using Vianigram.Kernel.Logging;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Subscribes to the shared MTProto updates stream and routes Telegram
    /// <c>updatePhoneCall</c> payloads into the Calls bounded context. This
    /// keeps call signaling reactive: outbound calls can move from Waiting to
    /// Ringing/Active when the peer accepts, and inbound calls surface through
    /// <c>ICallsApi.IncomingCall</c>.
    /// </summary>
    public sealed class CallsUpdatesProcessor : IDisposable
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

        private readonly Func<CallsApplication> _resolveCalls;
        private readonly IDisposable _subscription;
        private int _rawLogCount;
        private int _disposed;

        public CallsUpdatesProcessor(IUpdatesPort updates, Func<CallsApplication> resolveCalls)
        {
            if (updates == null) throw new ArgumentNullException("updates");
            if (resolveCalls == null) throw new ArgumentNullException("resolveCalls");

            _resolveCalls = resolveCalls;
            _subscription = updates.Subscribe(OnUpdateAsync);
            EarlyLog.Write("CallsUpdates", "subscribed to IUpdatesPort");
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { if (_subscription != null) _subscription.Dispose(); }
            catch { }
        }

        private async Task OnUpdateAsync(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8) return;

            try
            {
                int rawLogNo = Interlocked.Increment(ref _rawLogCount);
                if (rawLogNo <= 20)
                {
                    EarlyLog.Write("CallsUpdates", "raw push received len=" + bytes.Length
                        + " root=0x" + ReadLe32(bytes, 0).ToString("x8"));
                }

                bool matched = false;
                bool matchedSignaling = false;
                for (int offset = 0; offset + 8 <= bytes.Length; offset += 4)
                {
                    uint updateCtor = ReadLe32(bytes, offset);
                    if (updateCtor == CtorUpdatePhoneCallSignalingData)
                    {
                        long callId;
                        byte[] data;
                        if (TryReadSignalingData(bytes, offset + 4, out callId, out data))
                        {
                            matchedSignaling = true;
                            EarlyLog.Write("CallsUpdates", "updatePhoneCallSignalingData received offset=" + offset
                                + " callId=" + callId
                                + " data=" + (data == null ? 0 : data.Length) + "B");

                            CallsApplication signalingCalls = _resolveCalls();
                            if (signalingCalls == null)
                            {
                                EarlyLog.Write("CallsUpdates", "updatePhoneCallSignalingData skipped: CallsApplication unavailable");
                                continue;
                            }

                            var signalingResult = await signalingCalls
                                .ReceiveSignalingDataAsync(new Vianigram.Calls.Domain.ValueObjects.CallId(callId), data, CancellationToken.None)
                                .ConfigureAwait(false);
                            if (signalingResult.IsFail)
                            {
                                EarlyLog.Write("CallsUpdates", "updatePhoneCallSignalingData dispatch failed: " + signalingResult.Error);
                            }
                            else
                            {
                                EarlyLog.Write("CallsUpdates", "updatePhoneCallSignalingData dispatched");
                            }
                        }
                        continue;
                    }

                    if (updateCtor != CtorUpdatePhoneCall) continue;

                    int phoneCallOffset = offset + 4;
                    uint phoneCtor = ReadLe32(bytes, phoneCallOffset);
                    EarlyLog.Write("CallsUpdates", "updatePhoneCall candidate offset=" + offset
                        + " phoneCtor=0x" + phoneCtor.ToString("x8")
                        + " totalLen=" + bytes.Length);
                    if (!LooksLikePhoneCall(bytes, phoneCallOffset))
                    {
                        EarlyLog.Write("CallsUpdates", "updatePhoneCall skipped: unknown PhoneCall ctor=0x"
                            + phoneCtor.ToString("x8"));
                        Vianigram.Kernel.Telemetry.UnknownCtorTelemetry.Observe(
                            "Calls.UpdatesProcessor",
                            phoneCtor,
                            "PhoneCall payload after updatePhoneCall@offset=" + offset);
                        continue;
                    }

                    matched = true;
                    byte[] phoneCall = CopyTail(bytes, phoneCallOffset);
                    CallsApplication calls = _resolveCalls();
                    if (calls == null)
                    {
                        EarlyLog.Write("CallsUpdates", "updatePhoneCall skipped: CallsApplication unavailable");
                        return;
                    }

                    var result = await calls.UpdateHandler
                        .HandleAsync(new UpdateCallStateCommand(phoneCall), CancellationToken.None)
                        .ConfigureAwait(false);

                    if (result.IsFail)
                    {
                        EarlyLog.Write("CallsUpdates", "updatePhoneCall failed: " + result.Error);
                    }
                    else
                    {
                        EarlyLog.Write("CallsUpdates", "updatePhoneCall dispatched");
                    }
                }

                if (!matched && !matchedSignaling && rawLogNo <= 20)
                {
                    EarlyLog.Write("CallsUpdates", "raw push had no updatePhoneCall len=" + bytes.Length
                        + " root=0x" + ReadLe32(bytes, 0).ToString("x8"));
                }
            }
            catch (Exception ex)
            {
                EarlyLog.Write("CallsUpdates", "dispatch threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool LooksLikePhoneCall(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length) return false;
            uint ctor = ReadLe32(bytes, offset);
            return ctor == CtorPhoneCallEmpty
                || ctor == CtorPhoneCallWaiting
                || ctor == CtorPhoneCallRequested
                || ctor == CtorPhoneCallAccepted
                || ctor == CtorPhoneCall
                || ctor == CtorPhoneCallLegacy
                || ctor == CtorPhoneCallDiscarded;
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

        private static byte[] CopyTail(byte[] bytes, int offset)
        {
            int len = bytes.Length - offset;
            byte[] copy = new byte[len];
            Buffer.BlockCopy(bytes, offset, copy, 0, len);
            return copy;
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
    }
}
