// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// SRP-2048 challenge issued by <c>account.getPassword</c>, consumed by the
    /// <c>auth.checkPassword</c> step. Mirrors the public fields of
    /// <c>account.password</c> needed to compute M1.
    ///
    /// All buffers are server-issued opaque material — kept as raw byte arrays
    /// here because they are not key material per principles.md §M3 (the
    /// password itself never enters the domain; the server-side public mod
    /// values can travel as bytes).
    /// </summary>
    public sealed class SrpChallenge
    {
        public long SrpId { get; private set; }
        public byte[] CurrentAlgo { get; private set; }   // serialized passwordKdfAlgo*
        public byte[] Salt1 { get; private set; }
        public byte[] Salt2 { get; private set; }
        public int G { get; private set; }
        public byte[] P { get; private set; }              // 2048-bit big-endian prime
        public byte[] SrpB { get; private set; }           // server B value
        public string Hint { get; private set; }

        public SrpChallenge(
            long srpId,
            byte[] currentAlgo,
            byte[] salt1,
            byte[] salt2,
            int g,
            byte[] p,
            byte[] srpB,
            string hint)
        {
            if (salt1 == null) throw new ArgumentNullException("salt1");
            if (salt2 == null) throw new ArgumentNullException("salt2");
            if (p == null) throw new ArgumentNullException("p");
            if (srpB == null) throw new ArgumentNullException("srpB");

            SrpId = srpId;
            CurrentAlgo = currentAlgo;
            Salt1 = salt1;
            Salt2 = salt2;
            G = g;
            P = p;
            SrpB = srpB;
            Hint = hint;
        }
    }
}
