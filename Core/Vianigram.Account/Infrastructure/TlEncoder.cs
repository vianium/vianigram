// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vianigram.Account.Infrastructure
{
    /// <summary>
    /// Hand-written TL serialization for the RPCs needed by the auth
    /// flow (layer 214):
    ///   - auth.sendCode#a677244f
    ///   - auth.resendCode#cae47523
    ///   - auth.signIn#8d52a951        (was bcd51581 pre-flags; layer 169+ requires
    ///                                  a flagged variant. Sending the old ctor
    ///                                  with a flagged payload triggers
    ///                                  INPUT_REQUEST_TOO_LONG on the server.)
    ///   - auth.signUp#aac7b717
    ///   - auth.checkPassword#d18b4d16  (input is inputCheckPasswordSRP)
    ///   - account.getPassword#548a30f5
    ///
    /// The exact wire format is documented at
    /// https://core.telegram.org/schema (layer 158+).
    ///
    /// All functions return a <c>byte[]</c> ready to be passed to
    /// <see cref="Ports.Outbound.IMtProtoRpcPort.CallAsync"/>.
    /// </summary>
    internal static class TlEncoder
    {
        // Constructors.
        public const uint AuthSendCode = 0xa677244f;
        public const uint AuthResendCode = 0xcae47523;
        public const uint AuthSignIn = 0x8d52a951;
        public const uint AuthSignUp = 0xaac7b717;
        public const uint AuthCheckPassword = 0xd18b4d16;
        public const uint AccountGetPassword = 0x548a30f5;
        public const uint InputCheckPasswordSrp = 0xd27ff082;
        // Cross-DC authorisation transfer (used to authenticate a peer DC
        // after a primary login, e.g. handing the Sync layer's main
        // MtProtoChannel a session that auth.signIn issued on a different DC).
        //   auth.exportAuthorization#e5bfffcd dc_id:int = auth.ExportedAuthorization;
        //   auth.importAuthorization#a57a7dad id:long bytes:bytes = auth.Authorization;
        //   auth.exportedAuthorization#b434e2b8 id:long bytes:bytes = auth.ExportedAuthorization;
        public const uint AuthExportAuthorization = 0xe5bfffcd;
        public const uint AuthImportAuthorization = 0xa57a7dad;
        public const uint AuthExportedAuthorization = 0xb434e2b8;

        // help.getConfig#c4f9186b = Config;
        //
        // No-arg RPC documented at https://core.telegram.org/method/help.getConfig.
        // Returned Config object carries the canonical dc_options vector
        // that we persist via SqliteDcOptionsStore. Called once after the
        // first successful DC handshake so cold starts on subsequent
        // launches have multiple IPs per DC available.
        public const uint HelpGetConfig = 0xc4f9186bu;

        // Sub-records.
        public const uint CodeSettings = 0xad253d78;     // codeSettings#ad253d78 with no flags

        public static byte[] EncodeAuthSendCode(string phoneE164, int apiId, string apiHash)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AuthSendCode);
                WriteString(ms, NormalizePhoneNumberForAuth(phoneE164));
                WriteInt(ms, apiId);
                WriteString(ms, apiHash);
                // codeSettings#ad253d78: flags:#
                WriteUInt(ms, CodeSettings);
                WriteInt(ms, 0); // flags = 0 (no allow_flashcall, no current_number, no app_hash, etc.)
                return ms.ToArray();
            }
        }

        public static byte[] EncodeAuthResendCode(string phoneE164, string phoneCodeHash)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AuthResendCode);
                // auth.resendCode#cae47523 flags:# phone_number:string phone_code_hash:string
                WriteInt(ms, 0); // flags = 0 (no official-client reason)
                WriteString(ms, NormalizePhoneNumberForAuth(phoneE164));
                WriteString(ms, phoneCodeHash);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// auth.exportAuthorization#e5bfffcd dc_id:int = auth.ExportedAuthorization;
        ///
        /// Issued ON the home DC (where auth.signIn succeeded) to mint a
        /// short-lived blob the caller can hand to a peer DC via
        /// <see cref="EncodeAuthImportAuthorization"/>.
        /// </summary>
        public static byte[] EncodeAuthExportAuthorization(int targetDcId)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AuthExportAuthorization);
                WriteInt(ms, targetDcId);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// auth.importAuthorization#a57a7dad id:long bytes:bytes = auth.Authorization;
        ///
        /// Issued ON the peer DC, consuming the (id, bytes) pair returned by
        /// the home DC's auth.exportAuthorization. Server returns
        /// auth.authorization on success — same shape as auth.signIn so the
        /// caller can reuse <c>TlDecoder.DecodeAuthorization</c>.
        /// </summary>
        public static byte[] EncodeAuthImportAuthorization(long id, byte[] bytes)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AuthImportAuthorization);
                WriteLong(ms, id);
                WriteBytes(ms, bytes ?? new byte[0]);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeAuthSignIn(string phoneE164, string phoneCodeHash, string code)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AuthSignIn);
                // auth.signIn#8d52a951 flags:# phone_number:string phone_code_hash:string phone_code:flags.0?string email_verification:flags.1?EmailVerification
                // flags = bit 0 set: phone_code present (bit 1 reserved for email_verification, off here).
                WriteInt(ms, 1);
                WriteString(ms, NormalizePhoneNumberForAuth(phoneE164));
                WriteString(ms, phoneCodeHash);
                WriteString(ms, code);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeAuthSignUp(
            string phoneE164,
            string phoneCodeHash,
            string firstName,
            string lastName)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AuthSignUp);
                // auth.signUp#aac7b717 flags:# no_joined_notifications:flags.0?true
                // phone_number:string phone_code_hash:string first_name:string last_name:string
                WriteInt(ms, 1);
                WriteString(ms, NormalizePhoneNumberForAuth(phoneE164));
                WriteString(ms, phoneCodeHash);
                WriteString(ms, firstName ?? string.Empty);
                WriteString(ms, lastName ?? string.Empty);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// auth.checkPassword#d18b4d16 password:InputCheckPasswordSRP
        ///
        /// inputCheckPasswordSRP#d27ff082 srp_id:long A:bytes M1:bytes
        ///
        /// The caller supplies precomputed A and M1 buffers from the SRP port.
        /// </summary>
        public static byte[] EncodeAuthCheckPassword(long srpId, byte[] A, byte[] M1)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AuthCheckPassword);
                WriteUInt(ms, InputCheckPasswordSrp);
                WriteLong(ms, srpId);
                WriteBytes(ms, A ?? new byte[0]);
                WriteBytes(ms, M1 ?? new byte[0]);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeAccountGetPassword()
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AccountGetPassword);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// <c>help.getConfig#c4f9186b = Config;</c> — no-arg RPC.
        /// Response carries the canonical <c>dc_options</c> vector.
        /// </summary>
        public static byte[] EncodeHelpGetConfig()
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, HelpGetConfig);
                return ms.ToArray();
            }
        }

        // ---- account.registerDevice --------------------------------------
        //
        //   account.registerDevice#ec86017a flags:#
        //       no_muted:flags.0?true
        //       token_type:int
        //       token:string
        //       app_sandbox:Bool
        //       secret:bytes
        //       other_uids:Vector<long>
        //   = Bool;
        //
        // For Windows Push Notification Services we set token_type=8,
        // token=channel-uri, app_sandbox=false, secret=empty,
        // other_uids=empty. Telegram pushes
        // raw notifications to the channel URI; the platform OS hands the
        // payload to our RawNotificationTask which decrypts via the
        // session secret and shows a toast.
        public const uint AccountRegisterDevice = 0xec86017au;
        public const uint AccountUnregisterDevice = 0x6a0d3206u;
        public const uint BoolTrue = 0x997275b5u;
        public const uint BoolFalse = 0xbc799737u;

        /// <summary>
        /// Build an <c>account.registerDevice</c> request for a WNS
        /// channel. Caller passes the URI from
        /// <c>PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync()</c>.
        /// </summary>
        public static byte[] EncodeAccountRegisterDevice(
            int tokenType,
            string token,
            bool appSandbox,
            byte[] secret,
            long[] otherUids,
            bool noMuted)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AccountRegisterDevice);
                int flags = 0;
                if (noMuted) flags |= 1; // flags.0
                WriteInt(ms, flags);
                WriteInt(ms, tokenType);
                WriteString(ms, token ?? string.Empty);
                WriteUInt(ms, appSandbox ? BoolTrue : BoolFalse);
                WriteBytes(ms, secret ?? new byte[0]);

                // Vector<long>
                WriteUInt(ms, 0x1cb5c415u);
                int n = otherUids == null ? 0 : otherUids.Length;
                WriteInt(ms, n);
                for (int i = 0; i < n; i++) WriteLong(ms, otherUids[i]);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Build an <c>account.unregisterDevice</c> request for a WNS
        /// channel. Called on logout to stop receiving pushes for the
        /// signed-out account.
        ///
        ///   account.unregisterDevice#6a0d3206 token_type:int token:string other_uids:Vector<long> = Bool;
        /// </summary>
        public static byte[] EncodeAccountUnregisterDevice(int tokenType, string token, long[] otherUids)
        {
            using (var ms = new MemoryStream())
            {
                WriteUInt(ms, AccountUnregisterDevice);
                WriteInt(ms, tokenType);
                WriteString(ms, token ?? string.Empty);
                WriteUInt(ms, 0x1cb5c415u); // Vector<long>
                int n = otherUids == null ? 0 : otherUids.Length;
                WriteInt(ms, n);
                for (int i = 0; i < n; i++) WriteLong(ms, otherUids[i]);
                return ms.ToArray();
            }
        }

        // ---- Primitives ----

        private static string NormalizePhoneNumberForAuth(string phoneE164)
        {
            if (string.IsNullOrEmpty(phoneE164))
            {
                return string.Empty;
            }

            string phone = phoneE164.Trim();
            if (phone.Length > 0 && phone[0] == '+')
            {
                return phone.Substring(1);
            }

            return phone;
        }

        public static void WriteInt(Stream s, int value)
        {
            s.WriteByte((byte)(value & 0xff));
            s.WriteByte((byte)((value >> 8) & 0xff));
            s.WriteByte((byte)((value >> 16) & 0xff));
            s.WriteByte((byte)((value >> 24) & 0xff));
        }

        public static void WriteUInt(Stream s, uint value)
        {
            s.WriteByte((byte)(value & 0xff));
            s.WriteByte((byte)((value >> 8) & 0xff));
            s.WriteByte((byte)((value >> 16) & 0xff));
            s.WriteByte((byte)((value >> 24) & 0xff));
        }

        public static void WriteLong(Stream s, long value)
        {
            for (int i = 0; i < 8; i++)
            {
                s.WriteByte((byte)((value >> (8 * i)) & 0xff));
            }
        }

        public static void WriteString(Stream s, string value)
        {
            byte[] bytes = value == null ? new byte[0] : Encoding.UTF8.GetBytes(value);
            WriteBytes(s, bytes);
        }

        /// <summary>
        /// TL-bytes encoding: short form if length &lt; 254, long form otherwise.
        /// Always padded to a 4-byte boundary.
        /// </summary>
        public static void WriteBytes(Stream s, byte[] bytes)
        {
            int len = bytes.Length;
            int total;

            if (len <= 253)
            {
                s.WriteByte((byte)len);
                total = 1 + len;
            }
            else
            {
                s.WriteByte(254);
                s.WriteByte((byte)(len & 0xff));
                s.WriteByte((byte)((len >> 8) & 0xff));
                s.WriteByte((byte)((len >> 16) & 0xff));
                total = 4 + len;
            }

            s.Write(bytes, 0, len);

            int rem = total % 4;
            if (rem != 0)
            {
                int pad = 4 - rem;
                for (int i = 0; i < pad; i++) s.WriteByte(0);
            }
        }

        // Used only by helpers/tests.
        public static byte[] WriteToArray(Action<MemoryStream> writer)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            using (var ms = new MemoryStream())
            {
                writer(ms);
                return ms.ToArray();
            }
        }
    }
}
