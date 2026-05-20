// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Wire-format flavour of an MTProxy secret. The three formats Telegram
    /// clients accept differ only in the leading flag byte (or absence of
    /// one) plus an optional ASCII SNI domain appended after the 16 raw
    /// secret bytes:
    ///
    ///   <c>Legacy</c>  — 16 raw bytes, no flag byte. Original MTProxy v1
    ///                    "obfuscated2" handshake.
    ///   <c>Secure</c>  — 0xDD flag byte + 16 raw bytes. Secure-only
    ///                    random-padding intermediate protocol (rejects
    ///                    clients without padding support).
    ///   <c>FakeTls</c> — 0xEE flag byte + 16 raw bytes + N-byte ASCII
    ///                    domain. Wraps the obfuscated transport inside a
    ///                    forged TLS 1.3 ClientHello whose SNI extension
    ///                    is the given domain.
    ///
    /// Integer values are pinned because they cross the WinRT boundary
    /// (Vianigram.MTProto.MtProxyRuntime.SetActiveProxy expects an int
    /// mode argument).
    /// </summary>
    public enum ProxySecretMode
    {
        Legacy  = 0,
        Secure  = 1,
        FakeTls = 2
    }
}
