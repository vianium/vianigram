// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Ports.Outbound
{
    /// <summary>
    /// Keeps per-call Diffie-Hellman state out of handlers. Outgoing calls
    /// first publish SHA256(g_a), then reveal g_a in phone.confirmCall after
    /// the peer sends g_b. Incoming calls publish g_b and later compute the
    /// shared key when the peer confirms with g_a.
    /// </summary>
    public interface ICallCryptoVault
    {
        Task<Result<byte[], CallError>> CreateOutgoingGAHashAsync(int randomId, CancellationToken ct);

        Result<Unit, CallError> BindOutgoingCall(int randomId, CallId callId);

        Result<Unit, CallError> RegisterIncomingGAHash(CallId callId, byte[] gAHash);

        Task<Result<byte[], CallError>> CreateIncomingGBAsync(CallId callId, CancellationToken ct);

        Task<Result<ConfirmCallMaterial, CallError>> AcceptPeerGBAsync(CallId callId, byte[] gB, CancellationToken ct);

        Task<Result<Unit, CallError>> ConfirmPeerGAOrBAsync(CallId callId, byte[] gAOrB, long expectedFingerprint, CancellationToken ct);

        long GetLocalFingerprint(CallId callId);

        CallKeyHandle GetSharedKeyHandle(CallId callId);

        void Drop(CallId callId);
    }

    public interface ICallCryptoCapabilityPort
    {
        bool CanExchangeCallKeys { get; }
        string UnavailableReason { get; }
    }

    public sealed class ConfirmCallMaterial
    {
        public ConfirmCallMaterial(byte[] gA, long keyFingerprint)
        {
            GA = CloneBytes(gA);
            KeyFingerprint = keyFingerprint;
        }

        public byte[] GA { get; private set; }
        public long KeyFingerprint { get; private set; }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source == null) return new byte[0];
            byte[] copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    /// <summary>
    /// Opaque reference to per-call secret material owned by the native
    /// crypto/VoIP runtime. Managed signaling can carry the handle across
    /// port boundaries, but it never sees the shared key bytes.
    /// </summary>
    public sealed class CallKeyHandle
    {
        public static readonly CallKeyHandle Empty = new CallKeyHandle(string.Empty);

        public CallKeyHandle(string value)
        {
            Value = value ?? string.Empty;
        }

        public string Value { get; private set; }

        public bool IsEmpty
        {
            get { return string.IsNullOrEmpty(Value); }
        }

        public override string ToString()
        {
            return IsEmpty ? "(empty)" : Value;
        }
    }
}
