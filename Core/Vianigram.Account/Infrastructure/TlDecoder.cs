// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Account.Domain.ValueObjects;

namespace Vianigram.Account.Infrastructure
{
    /// <summary>
    /// Hand-written TL deserialization for the auth-flow responses consumed
    /// by the Account bounded context:
    ///   - auth.sentCode#5e002502
    ///   - auth.authorization#2ea2c0d4
    ///   - auth.authorizationSignUpRequired#44747e9a
    ///   - account.password#957b50fb
    ///   - rpc_error#2144ca19
    ///
    /// The decoder is permissive: unknown trailing fields are tolerated by
    /// the higher-layer handler which only consumes the fields it needs.
    /// </summary>
    internal static class TlDecoder
    {
        // Constructors we recognise.
        public const uint AuthSentCode = 0x5e002502;
        public const uint AuthAuthorization = 0x2ea2c0d4;
        public const uint AuthAuthorizationSignUpRequired = 0x44747e9a;
        public const uint AccountPassword = 0x957b50fb;
        public const uint RpcError = 0x2144ca19;
        public const uint BoolTrue = 0x997275b5;
        public const uint BoolFalse = 0xbc799737;

        // Subtypes inside auth.sentCode.type:
        public const uint SentCodeTypeApp = 0x3dbb5986;
        public const uint SentCodeTypeSms = 0xc000bba2;
        public const uint SentCodeTypeCall = 0x5353e5a7;
        public const uint SentCodeTypeFlashCall = 0xab03c6d9;
        public const uint SentCodeTypeMissedCall = 0x82006484;

        // Subtypes inside account.password.current_algo:
        public const uint PasswordKdfAlgoSha256Sha256Pbkdf2HmacSha512Iter100000Sha256ModPow = 0x3a912d4a;
        public const uint PasswordKdfAlgoUnknown = 0xd45ab096;

        // ---- High-level decoders ----

        public sealed class SentCodeDecoded
        {
            public string PhoneCodeHash;
            public SentCodeType Type;
            public SentCodeType? NextType;
            public int? Timeout;
        }

        public sealed class AuthorizationDecoded
        {
            public bool SignupRequired;
            public long UserId;
        }

        public sealed class AccountPasswordDecoded
        {
            public bool HasPassword;
            public byte[] CurrentAlgoBlob;
            public byte[] Salt1;
            public byte[] Salt2;
            public int G;
            public byte[] P;
            public byte[] SrpB;
            public long SrpId;
            public string Hint;
        }

        public sealed class RpcErrorDecoded
        {
            public int ErrorCode;
            public string ErrorMessage;
        }

        public static uint PeekConstructor(byte[] response)
        {
            if (response == null || response.Length < 4)
            {
                return 0;
            }

            return (uint)(response[0] | (response[1] << 8) | (response[2] << 16) | (response[3] << 24));
        }

        public static SentCodeDecoded DecodeSentCode(byte[] response)
        {
            using (var ms = new MemoryStream(response))
            {
                uint ctor = ReadUInt(ms);
                if (ctor != AuthSentCode)
                {
                    return null;
                }

                int flags = ReadInt(ms);
                uint typeCtor = ReadUInt(ms);
                var result = new SentCodeDecoded { Type = MapSentCodeType(typeCtor) };

                SkipSentCodeTypeBody(ms, typeCtor);

                result.PhoneCodeHash = ReadString(ms);

                // flags.1 = next_type, flags.2 = timeout
                if ((flags & 0x02) != 0)
                {
                    result.NextType = MapCodeType(ReadUInt(ms));
                }
                if ((flags & 0x04) != 0) result.Timeout = ReadInt(ms);

                return result;
            }
        }

        public static AuthorizationDecoded DecodeAuthorization(byte[] response)
        {
            uint ctor = PeekConstructor(response);
            if (ctor == AuthAuthorizationSignUpRequired)
            {
                return new AuthorizationDecoded { SignupRequired = true, UserId = 0 };
            }

            if (ctor != AuthAuthorization)
            {
                return null;
            }

            using (var ms = new MemoryStream(response))
            {
                ReadUInt(ms); // ctor
                int flags = ReadInt(ms);
                if ((flags & 0x01) != 0) ReadInt(ms); // setup_password_required
                if ((flags & 0x02) != 0) ReadInt(ms); // otherwise_relogin_days
                if ((flags & 0x08) != 0) ReadInt(ms); // tmp_sessions

                // user:User — we only need the id field (long). User constructor is variable;
                // reading the constructor + flags + id long is sufficient for a lightweight hint.
                uint userCtor = ReadUInt(ms);
                int userFlags = ReadInt(ms);
                long userId = ReadLong(ms);

                return new AuthorizationDecoded { SignupRequired = false, UserId = userId };
            }
        }

        public static AccountPasswordDecoded DecodeAccountPassword(byte[] response)
        {
            using (var ms = new MemoryStream(response))
            {
                uint ctor = ReadUInt(ms);
                if (ctor != AccountPassword) return null;

                int flags = ReadInt(ms);
                bool hasPassword = (flags & 0x04) != 0;
                var result = new AccountPasswordDecoded { HasPassword = hasPassword };

                if (hasPassword)
                {
                    // current_algo:PasswordKdfAlgo — we copy the raw subtree bytes
                    // (constructor + parameters) so the SRP adapter can reuse them.
                    long algoStart = ms.Position;
                    uint algoCtor = ReadUInt(ms);
                    if (algoCtor == PasswordKdfAlgoSha256Sha256Pbkdf2HmacSha512Iter100000Sha256ModPow)
                    {
                        result.Salt1 = ReadBytes(ms);
                        result.Salt2 = ReadBytes(ms);
                        result.G = ReadInt(ms);
                        result.P = ReadBytes(ms);
                    }

                    long algoEnd = ms.Position;
                    result.CurrentAlgoBlob = new byte[algoEnd - algoStart];
                    long save = ms.Position;
                    ms.Position = algoStart;
                    ms.Read(result.CurrentAlgoBlob, 0, result.CurrentAlgoBlob.Length);
                    ms.Position = save;

                    result.SrpB = ReadBytes(ms);
                    result.SrpId = ReadLong(ms);
                }

                if ((flags & 0x01) != 0) result.Hint = ReadString(ms);
                // remaining fields: email_unconfirmed_pattern, new_algo, new_secure_algo, secure_random
                // are not needed for sign-in.

                return result;
            }
        }

        public static RpcErrorDecoded DecodeRpcError(byte[] response)
        {
            using (var ms = new MemoryStream(response))
            {
                uint ctor = ReadUInt(ms);
                if (ctor != RpcError) return null;
                return new RpcErrorDecoded
                {
                    ErrorCode = ReadInt(ms),
                    ErrorMessage = ReadString(ms)
                };
            }
        }

        // ---- Primitives ----

        public static int ReadInt(Stream s)
        {
            int b0 = s.ReadByte();
            int b1 = s.ReadByte();
            int b2 = s.ReadByte();
            int b3 = s.ReadByte();
            return (b0) | (b1 << 8) | (b2 << 16) | (b3 << 24);
        }

        public static uint ReadUInt(Stream s)
        {
            return (uint)ReadInt(s);
        }

        public static long ReadLong(Stream s)
        {
            long acc = 0;
            for (int i = 0; i < 8; i++)
            {
                int b = s.ReadByte();
                if (b < 0) throw new EndOfStreamException("ReadLong");
                acc |= ((long)b) << (8 * i);
            }

            return acc;
        }

        public static byte[] ReadBytes(Stream s)
        {
            int first = s.ReadByte();
            if (first < 0) throw new EndOfStreamException("ReadBytes len");
            int len;
            int prefix;

            if (first == 254)
            {
                int b1 = s.ReadByte();
                int b2 = s.ReadByte();
                int b3 = s.ReadByte();
                len = b1 | (b2 << 8) | (b3 << 16);
                prefix = 4;
            }
            else
            {
                len = first;
                prefix = 1;
            }

            var buf = new byte[len];
            int read = 0;
            while (read < len)
            {
                int n = s.Read(buf, read, len - read);
                if (n <= 0) throw new EndOfStreamException("ReadBytes body");
                read += n;
            }

            int total = prefix + len;
            int rem = total % 4;
            if (rem != 0)
            {
                int pad = 4 - rem;
                for (int i = 0; i < pad; i++)
                {
                    if (s.ReadByte() < 0) throw new EndOfStreamException("ReadBytes pad");
                }
            }

            return buf;
        }

        public static string ReadString(Stream s)
        {
            byte[] bytes = ReadBytes(s);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        private static SentCodeType MapSentCodeType(uint ctor)
        {
            switch (ctor)
            {
                case SentCodeTypeApp: return SentCodeType.App;
                case SentCodeTypeSms: return SentCodeType.Sms;
                case SentCodeTypeCall: return SentCodeType.Call;
                case SentCodeTypeFlashCall: return SentCodeType.FlashCall;
                case SentCodeTypeMissedCall: return SentCodeType.Call;
                default: return SentCodeType.Sms;
            }
        }

        private static SentCodeType MapCodeType(uint ctor)
        {
            switch (ctor)
            {
                case 0x72a3158c: return SentCodeType.Sms;       // auth.codeTypeSms
                case 0x741cd3e3: return SentCodeType.Call;      // auth.codeTypeCall
                case 0x226ccefb: return SentCodeType.FlashCall; // auth.codeTypeFlashCall
                default: return SentCodeType.Sms;
            }
        }

        private static void SkipSentCodeTypeBody(Stream s, uint typeCtor)
        {
            if (typeCtor == SentCodeTypeSms || typeCtor == SentCodeTypeApp ||
                typeCtor == SentCodeTypeCall)
            {
                ReadInt(s); // length
            }
            else if (typeCtor == SentCodeTypeMissedCall)
            {
                ReadString(s); // prefix
                ReadInt(s);    // length
            }
            else if (typeCtor == SentCodeTypeFlashCall)
            {
                ReadString(s); // pattern
            }
        }
    }
}
