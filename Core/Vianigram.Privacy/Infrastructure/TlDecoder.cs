// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the Privacy response shapes
    /// this context consumes. Mirrors the per-context approach used in
    /// <c>Vianigram.Settings</c>, <c>Vianigram.Notifications</c>,
    /// <c>Vianigram.Stickers</c> and <c>Vianigram.Search</c>.
    ///
    /// <para><b>Supported response constructors</b>:</para>
    /// <list type="bullet">
    ///   <item><description><c>account.privacyRules#50a04e45</c> rules:Vector&lt;PrivacyRule&gt; chats:Vector&lt;Chat&gt; users:Vector&lt;User&gt;</description></item>
    ///   <item><description><c>account.authorizations#4bff8ea0</c> authorization_ttl_days:int authorizations:Vector&lt;Authorization&gt;</description></item>
    ///   <item><description><c>authorization#ad01d61d</c> flags:# current:flags.0?true official_app:flags.1?true password_pending:flags.2?true encrypted_requests_disabled:flags.3?true call_requests_disabled:flags.4?true unconfirmed:flags.5?true hash:long device_model:string platform:string system_version:string api_id:int app_name:string app_version:string date_created:int date_active:int ip:string country:string region:string</description></item>
    ///   <item><description>Bool — <c>boolFalse#bc799737</c> / <c>boolTrue#997275b5</c></description></item>
    /// </list>
    ///
    /// <para><b>Limitation</b>: V1 does NOT decode the chats / users
    /// trailing vectors of <c>account.privacyRules</c>. The first vector
    /// (privacy rules) carries everything we need; the trailing chats / users
    /// are skipped via length-tolerant fallback (we stop at end-of-buffer).</para>
    /// </summary>
    internal static class TlDecoder
    {
        // ---- TL constructor ids ----------------------------------------------

        public const uint CtorAccountPrivacyRules = 0x50a04e45;
        public const uint CtorAccountAuthorizations = 0x4bff8ea0;
        public const uint CtorAuthorization = 0xad01d61d;

        // PrivacyRule — server-emitted form (different ctors than InputPrivacyRule!)
        public const uint CtorPrivacyValueAllowAll = 0x65427b82;
        public const uint CtorPrivacyValueDisallowAll = 0x8b73e763;
        public const uint CtorPrivacyValueAllowContacts = 0xfffe1bac;
        public const uint CtorPrivacyValueDisallowContacts = 0xf888fa1a;
        public const uint CtorPrivacyValueAllowCloseFriends = 0xf7e8d89b;
        public const uint CtorPrivacyValueAllowUsers = 0xb8905fb2;
        public const uint CtorPrivacyValueDisallowUsers = 0xe4621141;
        public const uint CtorPrivacyValueAllowChatParticipants = 0x6b134e8e;
        public const uint CtorPrivacyValueDisallowChatParticipants = 0x41c87565;

        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        public const uint CtorVector = 0x1cb5c415;

        // ---- Public API -------------------------------------------------------

        /// <summary>
        /// Decode an <c>account.privacyRules#50a04e45</c> response. Only the
        /// <c>rules</c> vector is parsed; trailing chats/users vectors are
        /// ignored by V1 (they would only matter if the rule referenced
        /// specific user / chat ids — those are surfaced through the rule
        /// clauses themselves).
        /// </summary>
        public static PrivacyRule DecodePrivacyRules(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorAccountPrivacyRules)
                    throw new InvalidDataException("Unexpected account.privacyRules constructor: 0x" + ctor.ToString("x8"));

                IList<PrivacyClause> clauses = DecodePrivacyClauseVector(r);
                return PrivacyRule.Of(clauses);
            }
        }

        /// <summary>
        /// Decode an <c>account.authorizations#4bff8ea0</c> response. Returns
        /// every <c>authorization</c> in the vector — the order matches the
        /// server (most-recent-active first by convention).
        /// </summary>
        public static IList<ActiveSession> DecodeAuthorizations(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorAccountAuthorizations)
                    throw new InvalidDataException("Unexpected account.authorizations constructor: 0x" + ctor.ToString("x8"));

                // currently unused by the cache; consume to advance the stream
                int authorizationTtlDays = r.ReadInt32();
                if (authorizationTtlDays < 0) authorizationTtlDays = 0;

                uint vectorCtor = r.ReadUInt32();
                if (vectorCtor != CtorVector)
                    throw new InvalidDataException("Expected Vector ctor for authorizations, got 0x" + vectorCtor.ToString("x8"));
                int count = r.ReadInt32();
                if (count < 0 || count > 1000000) throw new InvalidDataException("vector length out of range: " + count);

                var list = new List<ActiveSession>(count);
                for (int i = 0; i < count; i++)
                {
                    list.Add(DecodeAuthorization(r));
                }
                return list;
            }
        }

        // ---- TL primitives ----------------------------------------------------

        private static IList<PrivacyClause> DecodePrivacyClauseVector(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector)
                throw new InvalidDataException("Expected Vector ctor for privacy rules, got 0x" + vectorCtor.ToString("x8"));
            int count = r.ReadInt32();
            if (count < 0 || count > 1000000) throw new InvalidDataException("vector length out of range: " + count);

            var list = new List<PrivacyClause>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(DecodePrivacyClause(r));
            }
            return list;
        }

        private static PrivacyClause DecodePrivacyClause(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorPrivacyValueAllowAll) return PrivacyClause.AllowAll();
            if (ctor == CtorPrivacyValueDisallowAll) return PrivacyClause.DisallowAll();
            if (ctor == CtorPrivacyValueAllowContacts) return PrivacyClause.AllowContacts();
            if (ctor == CtorPrivacyValueDisallowContacts) return PrivacyClause.DisallowContacts();
            if (ctor == CtorPrivacyValueAllowCloseFriends) return PrivacyClause.AllowCloseFriends();
            if (ctor == CtorPrivacyValueAllowUsers) return PrivacyClause.AllowUsers(ReadLongVector(r));
            if (ctor == CtorPrivacyValueDisallowUsers) return PrivacyClause.DisallowUsers(ReadLongVector(r));
            if (ctor == CtorPrivacyValueAllowChatParticipants) return PrivacyClause.AllowChats(ReadLongVector(r));
            if (ctor == CtorPrivacyValueDisallowChatParticipants) return PrivacyClause.DisallowChats(ReadLongVector(r));
            throw new InvalidDataException("Unknown PrivacyValue constructor: 0x" + ctor.ToString("x8"));
        }

        private static IList<long> ReadLongVector(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector)
                throw new InvalidDataException("Expected Vector ctor for ids, got 0x" + vectorCtor.ToString("x8"));
            int count = r.ReadInt32();
            if (count < 0 || count > 1000000) throw new InvalidDataException("vector length out of range: " + count);
            var list = new List<long>(count);
            for (int i = 0; i < count; i++) list.Add(r.ReadInt64());
            return list;
        }

        private static ActiveSession DecodeAuthorization(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor != CtorAuthorization)
                throw new InvalidDataException("Unexpected authorization constructor: 0x" + ctor.ToString("x8"));

            int flags = r.ReadInt32();
            bool current = (flags & (1 << 0)) != 0;

            long hash = r.ReadInt64();
            string deviceModel = ReadString(r);
            string platform = ReadString(r);
            string systemVersion = ReadString(r);
            int apiId = r.ReadInt32();
            if (apiId < 0) apiId = 0; // unused; consume to advance stream
            string appName = ReadString(r);
            string appVersion = ReadString(r);
            int dateCreated = r.ReadInt32();
            int dateActive = r.ReadInt32();
            string ip = ReadString(r);
            string country = ReadString(r);
            string region = ReadString(r);

            return new ActiveSession(
                hash: hash,
                deviceModel: deviceModel,
                platform: platform,
                systemVersion: systemVersion,
                appName: appName,
                appVersion: appVersion,
                dateCreated: TelegramEpoch.AddSeconds(dateCreated),
                dateActive: TelegramEpoch.AddSeconds(dateActive),
                ip: ip,
                country: country,
                region: region,
                current: current);
        }

        private static readonly DateTime TelegramEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static string ReadString(BinaryReader r)
        {
            byte[] bytes = ReadBytes(r);
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        private static byte[] ReadBytes(BinaryReader r)
        {
            byte first = r.ReadByte();
            int len;
            int prefixLen;
            if (first == 254)
            {
                byte b1 = r.ReadByte();
                byte b2 = r.ReadByte();
                byte b3 = r.ReadByte();
                len = b1 | (b2 << 8) | (b3 << 16);
                prefixLen = 4;
            }
            else
            {
                len = first;
                prefixLen = 1;
            }
            byte[] bytes = r.ReadBytes(len);
            int padding = (4 - ((prefixLen + len) % 4)) % 4;
            for (int i = 0; i < padding; i++) r.ReadByte();
            return bytes;
        }
    }
}
