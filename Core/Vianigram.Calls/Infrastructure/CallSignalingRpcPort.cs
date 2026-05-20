// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Application;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Infrastructure
{
    /// <summary>
    /// Managed RPC side of the native VoIP signaling bridge. Native engines
    /// emit opaque bytes; this adapter wraps them in phone.sendSignalingData
    /// using the existing authenticated MTProto channel.
    /// </summary>
    public sealed class CallSignalingRpcPort : ICallSignalingRpcPort
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;

        public CallSignalingRpcPort(IMtProtoRpcPort rpc, ILogger logger)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (logger == null) throw new ArgumentNullException("logger");
            _rpc = rpc;
            _log = new TimestampedLogger(logger, "Calls.SignalingRpc");
        }

        public async Task<Result<Unit, CallError>> SendSignalingDataAsync(
            CallId id,
            long accessHash,
            byte[] data,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (id.Value <= 0)
                return Result<Unit, CallError>.Fail(CallError.ProtocolError("signaling call id must be positive"));
            if (data == null || data.Length == 0)
                return Result<Unit, CallError>.Fail(CallError.ProtocolError("empty signaling payload"));

            byte[] request = TlEncoder.EncodeSendSignalingData(id.Value, accessHash, data);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
                return Result<Unit, CallError>.Fail(RpcErrorMapper.Map(rpcResult.Error));

            uint ctor = PeekCtor(rpcResult.Value);
            if (ctor != TlEncoder.CtorBoolTrue)
            {
                _log.Warn("phone.sendSignalingData returned ctor=0x" + ctor.ToString("x8"));
            }

            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        private static uint PeekCtor(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return 0;
            return (uint)bytes[0]
                | ((uint)bytes[1] << 8)
                | ((uint)bytes[2] << 16)
                | ((uint)bytes[3] << 24);
        }
    }
}
