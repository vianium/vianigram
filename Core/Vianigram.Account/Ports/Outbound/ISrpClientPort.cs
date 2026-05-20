// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Outbound port that computes the SRP-2048 public value and proof used
    /// by <c>auth.checkPassword</c>.
    ///
    /// Implementations must run the heavy modular exponentiation off the UI
    /// thread. Per principles.md §M3 the password and intermediate secrets
    /// must not leave the implementation; only the public <c>A</c> value and
    /// <c>M1</c> proof are returned.
    /// </summary>
    public interface ISrpClientPort
    {
        Task<Result<SrpProof, AccountError>> ComputeProofAsync(
            string password,
            SrpChallenge challenge,
            CancellationToken ct);
    }
}
