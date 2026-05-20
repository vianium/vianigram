// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;

namespace Vianigram.Privacy.Ports.Outbound
{
    /// <summary>
    /// Outbound port for the PIN hash function. Abstracted so the build can
    /// ship with a deterministic stub (<c>StubPasscodeHasher</c>) while
    /// the real adapter — PBKDF2-HMAC-SHA512 implemented through the
    /// <c>Vianium.Crypto</c> WinMD (the same underlying primitive used by
    /// <c>SrpClient</c>) — is wired in.
    ///
    /// <para><b>Contract</b>:</para>
    /// <list type="bullet">
    ///   <item><description><see cref="GenerateSalt"/> returns a fresh, cryptographically random salt of <see cref="SaltLength"/> bytes.</description></item>
    ///   <item><description><see cref="ComputeHash"/> deterministically produces a fixed-width hash for <c>(pin, salt)</c>.</description></item>
    ///   <item><description>The output of <see cref="ComputeHash"/> is what
    ///   <see cref="Domain.ValueObjects.PasscodeState"/> persists; the raw PIN
    ///   never leaves the application layer.</description></item>
    ///   <item><description>Implementations MUST be deterministic for a given
    ///   <c>(pin, salt)</c> pair so verify can recompute the digest.</description></item>
    /// </list>
    /// </summary>
    public interface IPasscodeHasher
    {
        /// <summary>Length of the salt produced by <see cref="GenerateSalt"/>.</summary>
        int SaltLength { get; }

        /// <summary>Cryptographically random salt for a fresh enable / change.</summary>
        byte[] GenerateSalt();

        /// <summary>
        /// Compute the deterministic hash for <paramref name="pin"/> +
        /// <paramref name="salt"/>. The combined output (salt + hash) is what
        /// gets stored — the salt is held inside the digest by the convention
        /// the implementation chooses; the stub prepends it.
        /// </summary>
        byte[] ComputeHash(string pin, byte[] salt);
    }

    /// <summary>
    /// Lightweight hint enum for telemetry — the application layer surfaces
    /// the hasher kind so the host can verify it has wired the production
    /// adapter in release builds.
    /// </summary>
    public static class PasscodeHasherKinds
    {
        public const string Stub = "stub";
        public const string Pbkdf2HmacSha512 = "pbkdf2-hmac-sha512";
    }
}
