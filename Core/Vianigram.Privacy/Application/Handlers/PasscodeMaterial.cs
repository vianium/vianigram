// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Application.Handlers
{
    /// <summary>
    /// Helpers for unpacking the salt + hash blob held inside a
    /// <see cref="PasscodeState"/>. The pack format is
    /// <c>[saltLen:int32 BE][salt][hash]</c>; <see cref="EnablePasscodeHandler.PackSaltAndHash"/>
    /// produces it and the verify path reverses it here.
    ///
    /// <para>Internal — the application layer is the only caller. The hash
    /// blob never crosses the inbound boundary.</para>
    /// </summary>
    internal static class PasscodeMaterial
    {
        public static bool TryUnpack(byte[] packed, out byte[] salt, out byte[] hash)
        {
            salt = null;
            hash = null;
            if (packed == null || packed.Length < 4) return false;
            int sLen = ((packed[0] & 0xFF) << 24)
                     | ((packed[1] & 0xFF) << 16)
                     | ((packed[2] & 0xFF) << 8)
                     | (packed[3] & 0xFF);
            if (sLen < 0 || sLen > packed.Length - 4) return false;
            salt = new byte[sLen];
            Array.Copy(packed, 4, salt, 0, sLen);
            int hLen = packed.Length - 4 - sLen;
            hash = new byte[hLen];
            Array.Copy(packed, 4 + sLen, hash, 0, hLen);
            return true;
        }

        /// <summary>Constant-time byte-array compare (timing-safe).</summary>
        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
