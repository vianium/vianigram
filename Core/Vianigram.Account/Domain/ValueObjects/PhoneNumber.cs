// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.Errors;
using Vianigram.Kernel.Result;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// E.164 normalized phone number. Validated: leading '+', followed by 7-15
    /// decimal digits. The international prefix is mandatory (no implicit
    /// country resolution at the Domain layer).
    /// </summary>
    public sealed class PhoneNumber : IEquatable<PhoneNumber>
    {
        public string E164 { get; private set; }

        private PhoneNumber(string e164)
        {
            E164 = e164;
        }

        public static Result<PhoneNumber, AccountError> TryParse(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return Result<PhoneNumber, AccountError>.Fail(
                    AccountError.InvalidPhoneNumber("phone is empty"));
            }

            // Strip common cosmetic separators before validating.
            string cleaned = raw.Trim();
            int outIdx = 0;
            char[] buffer = new char[cleaned.Length];
            for (int i = 0; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if (c == ' ' || c == '-' || c == '(' || c == ')' || c == '.')
                {
                    continue;
                }

                buffer[outIdx++] = c;
            }

            if (outIdx == 0)
            {
                return Result<PhoneNumber, AccountError>.Fail(
                    AccountError.InvalidPhoneNumber("phone is empty after normalization"));
            }

            string e164 = new string(buffer, 0, outIdx);
            if (e164[0] != '+')
            {
                return Result<PhoneNumber, AccountError>.Fail(
                    AccountError.InvalidPhoneNumber("phone must start with '+' (E.164)"));
            }

            int digitCount = 0;
            for (int i = 1; i < e164.Length; i++)
            {
                char c = e164[i];
                if (c < '0' || c > '9')
                {
                    return Result<PhoneNumber, AccountError>.Fail(
                        AccountError.InvalidPhoneNumber("phone contains non-digit"));
                }

                digitCount++;
            }

            if (digitCount < 7 || digitCount > 15)
            {
                return Result<PhoneNumber, AccountError>.Fail(
                    AccountError.InvalidPhoneNumber("phone digit count out of range (7..15)"));
            }

            return Result<PhoneNumber, AccountError>.Ok(new PhoneNumber(e164));
        }

        public bool Equals(PhoneNumber other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return string.Equals(E164, other.E164, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PhoneNumber);
        }

        public override int GetHashCode()
        {
            return E164 == null ? 0 : E164.GetHashCode();
        }

        public override string ToString()
        {
            return E164;
        }
    }
}
