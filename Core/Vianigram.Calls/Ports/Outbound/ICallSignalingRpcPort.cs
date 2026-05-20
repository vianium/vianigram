// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Ports.Outbound
{
    /// <summary>
    /// Outbound ACL for Telegram's VoIP signaling RPC. The media engine owns
    /// the signaling payload bytes; the managed MTProto channel owns
    /// authentication, transport and request dispatch.
    /// </summary>
    public interface ICallSignalingRpcPort
    {
        Task<Result<Unit, CallError>> SendSignalingDataAsync(
            CallId id,
            long accessHash,
            byte[] data,
            CancellationToken ct);
    }
}
