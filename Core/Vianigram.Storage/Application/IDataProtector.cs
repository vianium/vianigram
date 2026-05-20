// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Application
{
    /// <summary>
    /// Application-level port for at-rest encryption. Implementations wrap
    /// platform crypto so callers never touch <c>DataProtectionProvider</c>
    /// directly. See <c>docs/security/at-rest-encryption.md</c> §1.
    /// </summary>
    public interface IDataProtector
    {
        /// <summary>
        /// Encrypts <paramref name="plaintext"/> bound to the current user/device.
        /// Output is opaque ciphertext suitable for persistence.
        /// </summary>
        Task<byte[]> ProtectAsync(byte[] plaintext, CancellationToken ct);

        /// <summary>
        /// Decrypts a buffer previously produced by <see cref="ProtectAsync"/>.
        /// Throws if the ciphertext was protected on a different device or under
        /// a different user scope.
        /// </summary>
        Task<byte[]> UnprotectAsync(byte[] ciphertext, CancellationToken ct);
    }
}
