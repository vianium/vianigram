// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;
using Vianigram.Storage.Application;

namespace Vianigram.Storage.Infrastructure
{
    /// <summary>
    /// <see cref="IDataProtector"/> implementation backed by the WP8.1 platform
    /// <see cref="DataProtectionProvider"/> with scope <c>LOCAL=user</c>.
    /// Per <c>docs/security/at-rest-encryption.md</c> §1 this is the canonical
    /// (and only) crypto provider for at-rest material in Vianigram.
    /// <para>
    /// Note: <c>LOCAL=machine</c> and <c>WEBCREDENTIALS=*</c> scopes are
    /// explicitly forbidden — see policy §1 "Scopes" table.
    /// </para>
    /// </summary>
    public sealed class PlatformDataProtector : IDataProtector
    {
        private const string ProtectScope = "LOCAL=user";

        private readonly DataProtectionProvider _protect;
        private readonly DataProtectionProvider _unprotect;

        public PlatformDataProtector()
        {
            // The protect-side instance is constructed with the scope; the
            // unprotect-side instance must be parameterless because the scope
            // is recovered from the ciphertext header by the OS.
            _protect = new DataProtectionProvider(ProtectScope);
            _unprotect = new DataProtectionProvider();
        }

        public async Task<byte[]> ProtectAsync(byte[] plaintext, CancellationToken ct)
        {
            if (plaintext == null) throw new ArgumentNullException("plaintext");
            ct.ThrowIfCancellationRequested();

            IBuffer input = CryptographicBuffer.CreateFromByteArray(plaintext);
            IBuffer output = await _protect.ProtectAsync(input).AsTask(ct).ConfigureAwait(false);

            byte[] result;
            CryptographicBuffer.CopyToByteArray(output, out result);
            return result;
        }

        public async Task<byte[]> UnprotectAsync(byte[] ciphertext, CancellationToken ct)
        {
            if (ciphertext == null) throw new ArgumentNullException("ciphertext");
            ct.ThrowIfCancellationRequested();

            IBuffer input = CryptographicBuffer.CreateFromByteArray(ciphertext);
            IBuffer output = await _unprotect.UnprotectAsync(input).AsTask(ct).ConfigureAwait(false);

            byte[] result;
            CryptographicBuffer.CopyToByteArray(output, out result);
            return result;
        }
    }
}
