// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the Privacy RPC shapes this
    /// context issues. Mirrors the per-context approach used in
    /// <c>Vianigram.Settings</c>, <c>Vianigram.Notifications</c>,
    /// <c>Vianigram.Stickers</c> and <c>Vianigram.Search</c>.
    ///
    /// <para><b>Supported requests</b>:</para>
    /// <list type="bullet">
    ///   <item><description><c>account.getPrivacy#dadbc950</c> key:InputPrivacyKey = account.PrivacyRules</description></item>
    ///   <item><description><c>account.setPrivacy#c9f81ce8</c> key:InputPrivacyKey rules:Vector&lt;InputPrivacyRule&gt; = account.PrivacyRules</description></item>
    ///   <item><description><c>account.getAuthorizations#e320c158</c> = account.Authorizations</description></item>
    ///   <item><description><c>account.resetAuthorization#df77f3bc</c> hash:long = Bool</description></item>
    ///   <item><description><c>auth.resetAuthorizations#9fab0d1a</c> = Bool</description></item>
    /// </list>
    ///
    /// <para>All multi-byte integers are little-endian (TL convention).
    /// Vector uses the boxed constructor <c>0x1cb5c415</c> followed by an
    /// int32 length and the element payloads.</para>
    /// </summary>
    internal static class TlEncoder
    {
        // ---- TL constructor ids ----------------------------------------------

        public const uint CtorAccountGetPrivacy = 0xdadbc950;
        public const uint CtorAccountSetPrivacy = 0xc9f81ce8;
        public const uint CtorAccountGetAuthorizations = 0xe320c158;
        public const uint CtorAccountResetAuthorization = 0xdf77f3bc;
        public const uint CtorAuthResetAuthorizations = 0x9fab0d1a;

        // InputPrivacyKey
        public const uint CtorInputPrivacyKeyStatusTimestamp = 0x4f96cb18;
        public const uint CtorInputPrivacyKeyChatInvite = 0xbdfb0426;
        public const uint CtorInputPrivacyKeyPhoneCall = 0xfabadc5f;
        public const uint CtorInputPrivacyKeyPhoneP2P = 0xdb9e70d2;
        public const uint CtorInputPrivacyKeyForwards = 0xa4dd4c08;
        public const uint CtorInputPrivacyKeyProfilePhoto = 0x5719bacc;
        public const uint CtorInputPrivacyKeyPhoneNumber = 0x352dafa;
        public const uint CtorInputPrivacyKeyAddedByPhone = 0xd1219bdd;
        public const uint CtorInputPrivacyKeyVoiceMessages = 0xaee69d68;
        public const uint CtorInputPrivacyKeyAbout = 0x3823cc40;
        public const uint CtorInputPrivacyKeyBirthday = 0xd65a11cc;

        // InputPrivacyRule
        public const uint CtorInputPrivacyValueAllowAll = 0x184b35ce;
        public const uint CtorInputPrivacyValueDisallowAll = 0xd66b66c9;
        public const uint CtorInputPrivacyValueAllowContacts = 0xd09e07b;
        public const uint CtorInputPrivacyValueDisallowContacts = 0xba52007;
        public const uint CtorInputPrivacyValueAllowCloseFriends = 0x2f453e49;
        public const uint CtorInputPrivacyValueAllowUsers = 0x131cc67f;
        public const uint CtorInputPrivacyValueDisallowUsers = 0x90110467;
        public const uint CtorInputPrivacyValueAllowChatParticipants = 0x840649cf;
        public const uint CtorInputPrivacyValueDisallowChatParticipants = 0xe94f0f86;

        // InputUser — for AllowUsers / DisallowUsers vectors. V1 uses
        // inputUserSelf for the "self" sentinel and inputUser#f21158c6 for
        // explicit ids; access_hash defaults to 0 because the MTProto session
        // cache rewrites it on the way out.
        public const uint CtorInputUser = 0xf21158c6;
        public const uint CtorInputUserSelf = 0xf7c1b13f;

        public const uint CtorVector = 0x1cb5c415;

        // ---- Public API -------------------------------------------------------

        public static byte[] EncodeGetPrivacy(PrivacyKey key)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAccountGetPrivacy);
                w.Write(InputPrivacyKey(key));
                return ms.ToArray();
            }
        }

        public static byte[] EncodeSetPrivacy(PrivacyKey key, PrivacyRule rule)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAccountSetPrivacy);
                w.Write(InputPrivacyKey(key));
                w.Write(CtorVector);
                IList<PrivacyClause> clauses = rule.Clauses;
                w.Write(clauses.Count);
                for (int i = 0; i < clauses.Count; i++)
                {
                    WriteInputPrivacyValue(w, clauses[i]);
                }
                return ms.ToArray();
            }
        }

        public static byte[] EncodeGetAuthorizations()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAccountGetAuthorizations);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeResetAuthorization(long hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAccountResetAuthorization);
                w.Write(hash);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeResetAllAuthorizations()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAuthResetAuthorizations);
                return ms.ToArray();
            }
        }

        // ---- TL primitives ----------------------------------------------------

        private static uint InputPrivacyKey(PrivacyKey key)
        {
            switch (key)
            {
                case PrivacyKey.StatusTimestamp: return CtorInputPrivacyKeyStatusTimestamp;
                case PrivacyKey.ChatInvite:      return CtorInputPrivacyKeyChatInvite;
                case PrivacyKey.PhoneCall:       return CtorInputPrivacyKeyPhoneCall;
                case PrivacyKey.PhoneP2P:        return CtorInputPrivacyKeyPhoneP2P;
                case PrivacyKey.Forwards:        return CtorInputPrivacyKeyForwards;
                case PrivacyKey.ProfilePhoto:    return CtorInputPrivacyKeyProfilePhoto;
                case PrivacyKey.PhoneNumber:     return CtorInputPrivacyKeyPhoneNumber;
                case PrivacyKey.AddedByPhone:    return CtorInputPrivacyKeyAddedByPhone;
                case PrivacyKey.VoiceMessages:   return CtorInputPrivacyKeyVoiceMessages;
                case PrivacyKey.About:           return CtorInputPrivacyKeyAbout;
                case PrivacyKey.Birthday:        return CtorInputPrivacyKeyBirthday;
                default:
                    throw new ArgumentOutOfRangeException("key", "Unknown PrivacyKey: " + key);
            }
        }

        private static void WriteInputPrivacyValue(BinaryWriter w, PrivacyClause clause)
        {
            if (clause == null) throw new ArgumentNullException("clause");
            switch (clause.Audience)
            {
                case PrivacyAudience.Everyone:
                    w.Write(clause.Allow ? CtorInputPrivacyValueAllowAll : CtorInputPrivacyValueDisallowAll);
                    return;
                case PrivacyAudience.Contacts:
                    w.Write(clause.Allow ? CtorInputPrivacyValueAllowContacts : CtorInputPrivacyValueDisallowContacts);
                    return;
                case PrivacyAudience.CloseFriends:
                    // Only "allow" is defined for close friends.
                    w.Write(CtorInputPrivacyValueAllowCloseFriends);
                    return;
                case PrivacyAudience.Users:
                    w.Write(clause.Allow ? CtorInputPrivacyValueAllowUsers : CtorInputPrivacyValueDisallowUsers);
                    WriteInputUserVector(w, clause.Ids);
                    return;
                case PrivacyAudience.Chats:
                    w.Write(clause.Allow ? CtorInputPrivacyValueAllowChatParticipants : CtorInputPrivacyValueDisallowChatParticipants);
                    WriteLongVector(w, clause.Ids);
                    return;
                default:
                    throw new ArgumentOutOfRangeException("clause.Audience", "Unknown PrivacyAudience: " + clause.Audience);
            }
        }

        private static void WriteInputUserVector(BinaryWriter w, IList<long> ids)
        {
            w.Write(CtorVector);
            w.Write(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                w.Write(CtorInputUser);
                w.Write(ids[i]);
                w.Write((long)0); // access_hash — resolved by MTProto session cache
            }
        }

        private static void WriteLongVector(BinaryWriter w, IList<long> ids)
        {
            w.Write(CtorVector);
            w.Write(ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                w.Write(ids[i]);
            }
        }
    }
}
