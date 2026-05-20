// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Ports.Outbound;

namespace Vianigram.Account.Application.Handlers
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="AccountError"/>. The handler layer owns this mapping so the
    /// outbound port stays generic.
    /// </summary>
    internal static class AuthErrorMapper
    {
        public static AccountError Map(MtProtoRpcError err)
        {
            if (err == null) return AccountError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            // Native channel hints first.
            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return AccountError.PhoneNumberFlood(err.Parameter);
            }

            if (string.Equals(kind, "PhoneMigrate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "NetworkMigrate", StringComparison.OrdinalIgnoreCase))
            {
                return AccountError.DcMigrationRequired(err.Parameter);
            }

            if (string.Equals(kind, "AuthRestart", StringComparison.OrdinalIgnoreCase))
            {
                return AccountError.AuthRestart(message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return AccountError.NetworkError(message);
            }

            // Fall back to message string parsing for codes the native side did
            // not classify (some error strings are still passed through as-is).
            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return AccountError.PhoneNumberFlood(seconds);
                }
            }

            if (message.StartsWith("PHONE_MIGRATE_", StringComparison.Ordinal))
            {
                int dc;
                if (int.TryParse(message.Substring("PHONE_MIGRATE_".Length), out dc))
                {
                    return AccountError.DcMigrationRequired(dc);
                }
            }

            if (string.Equals(message, "PHONE_CODE_INVALID", StringComparison.Ordinal))
            {
                return AccountError.PhoneCodeInvalid(message);
            }

            if (string.Equals(message, "PHONE_CODE_EMPTY", StringComparison.Ordinal))
            {
                return AccountError.PhoneCodeInvalid(message);
            }

            if (string.Equals(message, "PHONE_CODE_EXPIRED", StringComparison.Ordinal))
            {
                return AccountError.PhoneCodeExpired(message);
            }

            if (string.Equals(message, "PHONE_NUMBER_INVALID", StringComparison.Ordinal))
            {
                return AccountError.InvalidPhoneNumber(message);
            }

            if (string.Equals(message, "PHONE_NUMBER_OCCUPIED", StringComparison.Ordinal))
            {
                return AccountError.NotInExpectedState("phone number is already registered; request a new code");
            }

            if (string.Equals(message, "FIRSTNAME_INVALID", StringComparison.Ordinal))
            {
                return AccountError.NotInExpectedState("first name is invalid");
            }

            if (string.Equals(message, "LASTNAME_INVALID", StringComparison.Ordinal))
            {
                return AccountError.NotInExpectedState("last name is invalid");
            }

            if (string.Equals(message, "SESSION_PASSWORD_NEEDED", StringComparison.Ordinal))
            {
                // Caller distinguishes 2FA flow from this signal; surface as a
                // domain-typed kind so the handler can branch on it.
                return new AccountError(AccountErrorKind.NotInExpectedState, message);
            }

            if (string.Equals(message, "PASSWORD_HASH_INVALID", StringComparison.Ordinal))
            {
                return AccountError.SrpPasswordInvalid(message);
            }

            if (string.Equals(message, "AUTH_KEY_UNREGISTERED", StringComparison.Ordinal) ||
                string.Equals(message, "SESSION_REVOKED", StringComparison.Ordinal) ||
                string.Equals(message, "SESSION_EXPIRED", StringComparison.Ordinal))
            {
                return AccountError.SessionExpired(message);
            }

            return AccountError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }

        /// <summary>True iff the message indicates the server wants 2FA via SRP.</summary>
        public static bool Is2faRequired(MtProtoRpcError err)
        {
            return err != null
                && err.Message != null
                && string.Equals(err.Message, "SESSION_PASSWORD_NEEDED", StringComparison.Ordinal);
        }
    }
}
