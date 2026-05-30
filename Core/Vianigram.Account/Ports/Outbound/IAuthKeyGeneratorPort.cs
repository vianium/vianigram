// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Kernel.Result;

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Outbound port for the MTProto DH handshake (auth_key generation).
    /// Wraps native <c>Vianigram.MTProto.MtProtoConnection.GenerateAuthKeyAsync</c>.
    /// </summary>
    public interface IAuthKeyGeneratorPort
    {
        Task<Result<AuthKeyRecord, AccountError>> GenerateAsync(string host, int port, CancellationToken ct);
        Task<Result<AuthKeyRecord, AccountError>> GenerateForDcAsync(string host, int port, int dcId, CancellationToken ct);
    }
}
