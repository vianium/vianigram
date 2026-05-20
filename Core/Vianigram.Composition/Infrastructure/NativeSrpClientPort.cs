// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Result;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class NativeSrpClientPort : ISrpClientPort
    {
        public async Task<Result<SrpProof, AccountError>> ComputeProofAsync(
            string password,
            SrpChallenge challenge,
            CancellationToken ct)
        {
            if (challenge == null)
            {
                return Result<SrpProof, AccountError>.Fail(
                    AccountError.NotInExpectedState("SRP challenge is null"));
            }
            if (string.IsNullOrEmpty(password))
            {
                return Result<SrpProof, AccountError>.Fail(
                    AccountError.SrpPasswordInvalid("password is empty"));
            }

            byte[] salt1 = challenge.Salt1 ?? new byte[0];
            byte[] salt2 = challenge.Salt2 ?? new byte[0];
            byte[] p = challenge.P ?? new byte[0];
            byte[] srpB = challenge.SrpB ?? new byte[0];

            try
            {
                var native = await Vianium.Crypto.SrpClient.ComputeProofAsync(
                        salt1, salt2, challenge.G, p, srpB, password)
                    .AsTask(ct)
                    .ConfigureAwait(false);

                if (native == null)
                {
                    return Result<SrpProof, AccountError>.Fail(
                        AccountError.NetworkError("SRP native call returned null"));
                }
                if (!native.Success)
                {
                    string msg = string.IsNullOrEmpty(native.ErrorMessage)
                        ? "SRP failure"
                        : native.ErrorMessage;
                    return Result<SrpProof, AccountError>.Fail(
                        AccountError.SrpPasswordInvalid(msg));
                }

                byte[] a = native.ProofA;
                byte[] m1 = native.ProofM1;
                if (a == null || a.Length != 256)
                {
                    return Result<SrpProof, AccountError>.Fail(
                        AccountError.NetworkError(
                            "SRP native call returned malformed A (len=" +
                            (a == null ? 0 : a.Length) + ")"));
                }
                if (m1 == null || m1.Length != 32)
                {
                    return Result<SrpProof, AccountError>.Fail(
                        AccountError.NetworkError(
                            "SRP native call returned malformed M1 (len=" +
                            (m1 == null ? 0 : m1.Length) + ")"));
                }

                return Result<SrpProof, AccountError>.Ok(new SrpProof(a, m1));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<SrpProof, AccountError>.Fail(
                    AccountError.NetworkError(
                        "SRP native call failed: " + ex.GetType().Name + ": " + ex.Message,
                        ex));
            }
        }
    }
}
