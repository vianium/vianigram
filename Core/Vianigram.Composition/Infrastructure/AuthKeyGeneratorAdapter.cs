// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AuthKeyGeneratorAdapter.cs
//
// Adapter that fronts the native MTProto DH handshake.
// Wraps Vianigram.MTProto.MtProtoConnection::GenerateAuthKeyAsync, which
// runs the req_pq_multi -> req_DH_params -> set_client_DH_params dance and
// returns an AuthKeyResult carrying the 256-byte auth_key, its 64-bit id,
// the initial server salt, and the server time offset.
//
// The handshake creates a transient TCP connection, performs the handshake,
// and is then closed — the resulting auth_key is durable (per Telegram
// spec) and can be reused across many subsequent MtProtoChannel sessions
// until the server invalidates it. Composition persists it via the Account
// IAuthKeyStore (which is in turn bridged to the encrypted JsonAuthKeyStore
// in Vianigram.Storage).
//
// All errors map to AccountError.NetworkError; the Account application
// layer handles retries and FloodWait windows (handshake itself doesn't
// surface FloodWait — that comes later in auth.sendCode).

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Result;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class AuthKeyGeneratorAdapter : IAuthKeyGeneratorPort
    {
        // Interface-shaped entry point. Defaults to dcId=2 (Telegram's home
        // DC) so callers that don't track migrations still work — when an
        // MTProxy is active the proxy will route to DC#2, which is the
        // common case for the pre-login flow.
        public Task<Result<AuthKeyRecord, AccountError>> GenerateAsync(
            string host, int port, CancellationToken ct)
        {
            return GenerateForDcAsync(host, port, /* dcId = */ 2, ct);
        }

        // DC-aware overload. The dcId is embedded in the obfuscated MTProxy
        // init packet so the proxy routes to the correct upstream DC. On
        // the direct-dial path the parameter is ignored — the native
        // MtProtoConnection.ConnectWithDcAsync just hands it down to the
        // transport factory, which only consults it on the MTProxy branch.
        public async Task<Result<AuthKeyRecord, AccountError>> GenerateForDcAsync(
            string host, int port, int dcId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(host)) throw new ArgumentException("host required", "host");
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException("port");

            Vianigram.MTProto.MtProtoConnection conn = null;
            Vianigram.MTProto.AuthKeyResult result = null;
            try
            {
                conn = await Vianigram.MTProto.MtProtoConnection
                    .ConnectWithDcAsync(host, port, dcId > 0 ? dcId : 2)
                    .AsTask(ct)
                    .ConfigureAwait(false);

                if (conn == null)
                {
                    return Result<AuthKeyRecord, AccountError>.Fail(
                        AccountError.NetworkError("ConnectAsync returned null."));
                }

                result = await conn
                    .GenerateAuthKeyAsync()
                    .AsTask(ct)
                    .ConfigureAwait(false);

                if (result == null)
                {
                    return Result<AuthKeyRecord, AccountError>.Fail(
                        AccountError.NetworkError("GenerateAuthKeyAsync returned null."));
                }

                if (!result.Success)
                {
                    return Result<AuthKeyRecord, AccountError>.Fail(
                        AccountError.NetworkError(string.IsNullOrEmpty(result.ErrorMessage)
                            ? "DH handshake failed (no detail)."
                            : result.ErrorMessage));
                }

                if (result.AuthKeyBytes == null || result.AuthKeyBytes.Length != 256)
                {
                    return Result<AuthKeyRecord, AccountError>.Fail(
                        AccountError.NetworkError("DH handshake returned malformed auth_key."));
                }

                var record = new AuthKeyRecord
                {
                    AuthKey = CloneBytes(result.AuthKeyBytes),
                    AuthKeyId = result.AuthKeyId,
                    ServerSalt = result.InitialServerSalt,
                    ServerTimeOffset = result.ServerTimeOffset
                };
                return Result<AuthKeyRecord, AccountError>.Ok(record);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<AuthKeyRecord, AccountError>.Fail(
                    AccountError.NetworkError(ex.GetType().Name + ": " + ex.Message, ex));
            }
            finally
            {
                if (result != null && result.AuthKeyBytes != null)
                {
                    Array.Clear(result.AuthKeyBytes, 0, result.AuthKeyBytes.Length);
                }
                if (conn != null)
                {
                    try { conn.Close(); }
                    catch (Exception) { /* swallow — Close failures are not actionable */ }
                }
            }
        }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source == null) return null;
            byte[] copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
