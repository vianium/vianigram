// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Public client proof for Telegram <c>inputCheckPasswordSRP</c>.
    /// <c>A</c> is the 2048-bit client public value and <c>M1</c> is the
    /// 32-byte SRP proof. Password material and intermediate secrets stay
    /// inside the SRP port implementation.
    /// </summary>
    public sealed class SrpProof
    {
        public byte[] A { get; private set; }
        public byte[] M1 { get; private set; }

        public SrpProof(byte[] a, byte[] m1)
        {
            if (a == null) throw new ArgumentNullException("a");
            if (m1 == null) throw new ArgumentNullException("m1");
            if (a.Length != 256) throw new ArgumentException("SRP A must be 256 bytes", "a");
            if (m1.Length != 32) throw new ArgumentException("SRP M1 must be 32 bytes", "m1");

            A = (byte[])a.Clone();
            M1 = (byte[])m1.Clone();
        }
    }
}
