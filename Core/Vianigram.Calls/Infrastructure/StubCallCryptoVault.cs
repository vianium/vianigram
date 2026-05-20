// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Infrastructure
{
    /// <summary>
    /// Defensive fallback used only by legacy compositions. Production
    /// wiring should provide a real call crypto runtime; otherwise calls are
    /// unavailable before any Telegram RPC is sent.
    /// </summary>
    public sealed class StubCallCryptoVault : ICallCryptoVault, ICallCryptoCapabilityPort
    {
        public const string Reason =
            "Telegram call crypto is unavailable in this fallback build (g_a/g_b/key_fingerprint unavailable)";

        public bool CanExchangeCallKeys { get { return false; } }
        public string UnavailableReason { get { return Reason; } }

        public Task<Result<byte[], CallError>> CreateOutgoingGAHashAsync(int randomId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<byte[], CallError>.Fail(CallError.ProtocolError(Reason)));
        }

        public Result<Unit, CallError> BindOutgoingCall(int randomId, CallId callId)
        {
            return Result<Unit, CallError>.Fail(CallError.ProtocolError(Reason));
        }

        public Result<Unit, CallError> RegisterIncomingGAHash(CallId callId, byte[] gAHash)
        {
            return Result<Unit, CallError>.Fail(CallError.ProtocolError(Reason));
        }

        public Task<Result<byte[], CallError>> CreateIncomingGBAsync(CallId callId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<byte[], CallError>.Fail(CallError.ProtocolError(Reason)));
        }

        public Task<Result<ConfirmCallMaterial, CallError>> AcceptPeerGBAsync(CallId callId, byte[] gB, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<ConfirmCallMaterial, CallError>.Fail(CallError.ProtocolError(Reason)));
        }

        public Task<Result<Unit, CallError>> ConfirmPeerGAOrBAsync(CallId callId, byte[] gAOrB, long expectedFingerprint, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<Unit, CallError>.Fail(CallError.ProtocolError(Reason)));
        }

        public long GetLocalFingerprint(CallId callId)
        {
            return 0;
        }

        public CallKeyHandle GetSharedKeyHandle(CallId callId)
        {
            return CallKeyHandle.Empty;
        }

        public void Drop(CallId callId)
        {
        }
    }
}
