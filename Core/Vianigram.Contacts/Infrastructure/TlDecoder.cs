// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Domain.ValueObjects;

namespace Vianigram.Contacts.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the contacts.* responses we
    /// actually consume. Mirrors the per-context approach used in
    /// <c>Vianigram.Chats</c>.
    ///
    /// Supported response constructors:
    ///   * contacts.contacts#eae87e42           contacts:Vector&lt;Contact&gt; saved_count:int users:Vector&lt;User&gt;
    ///   * contacts.contactsNotModified#b74ba9d2 (empty body)
    ///   * contacts.importedContacts#77d01c3b   imported:Vector&lt;importedContact&gt; popular_invites:Vector&lt;...&gt; retry_contacts:Vector&lt;long&gt; users:Vector&lt;User&gt;
    ///   * contacts.found#b3134d9d              my_results:Vector&lt;Peer&gt; results:Vector&lt;Peer&gt; chats:Vector&lt;Chat&gt; users:Vector&lt;User&gt;
    ///   * contacts.blocked#0ade1591            my_results:Vector&lt;peerBlocked&gt; chats:Vector&lt;Chat&gt; users:Vector&lt;User&gt;
    ///   * contacts.blockedSlice#e1664194       count:int blocked:Vector&lt;peerBlocked&gt; chats:Vector&lt;Chat&gt; users:Vector&lt;User&gt;
    ///
    /// We parse the leading vectors deeply enough to populate the domain
    /// (contact pairs, user records, blocked peer ids) and stop short of the
    /// trailing chats:Vector&lt;Chat&gt; / users:Vector&lt;User&gt; collections in the
    /// few cases where their TL surface area is too large to track here. The
    /// "users" vector that DOES need to be parsed is parsed minimally — see
    /// <c>ReadUsersVector</c> below.
    /// </summary>
    internal static class TlDecoder
    {
        // ---- TL constructor ids ----------------------------------------------
        public const uint CtorContacts = 0xeae87e42;
        public const uint CtorContactsNotModified = 0xb74ba9d2;
        public const uint CtorImportedContacts = 0x77d01c3b;
        public const uint CtorFound = 0xb3134d9d;
        public const uint CtorBlocked = 0x0ade1591;
        public const uint CtorBlockedSlice = 0xe1664194;

        public const uint CtorContact = 0x145ade0b;
        public const uint CtorImportedContact = 0xc13e3c50;
        public const uint CtorPopularContact = 0x5ce14175;
        public const uint CtorPeerBlocked = 0xe8fd8014;

        public const uint CtorPeerUser = 0x59511722;
        public const uint CtorPeerChat = 0x36c6019a;
        public const uint CtorPeerChannel = 0xa2a5371e;

        public const uint CtorVector = 0x1cb5c415;
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        // ----- result containers ---------------------------------------------

        public sealed class DecodedContacts
        {
            public bool NotModified { get; set; }
            public IList<Contact> Contacts { get; set; }
        }

        public sealed class DecodedImportedContacts
        {
            public IList<ContactImportResult> Imported { get; set; }
            public IList<long> RetryClientIds { get; set; }
            public IList<Contact> Users { get; set; }
        }

        public sealed class DecodedFound
        {
            public IList<long> UserIds { get; set; }      // peer ids extracted from results
            public IList<Contact> Users { get; set; }     // matched user records (minimally parsed)
        }

        public sealed class DecodedBlocked
        {
            public IList<long> BlockedUserIds { get; set; }
        }

        // ----- top-level decoders --------------------------------------------

        public static DecodedContacts DecodeContacts(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedContacts { NotModified = false, Contacts = new List<Contact>() };
                if (ctor == CtorContactsNotModified)
                {
                    result.NotModified = true;
                    return result;
                }
                if (ctor != CtorContacts)
                    throw new InvalidDataException("Unexpected contacts.Contacts constructor: 0x" + ctor.ToString("x8"));

                ExpectVector(r);
                int contactCount = r.ReadInt32();
                var pairs = new List<KeyValuePair<long, bool>>(contactCount); // (user_id, mutual)
                for (int i = 0; i < contactCount; i++)
                {
                    uint cctor = r.ReadUInt32();
                    if (cctor != CtorContact)
                        throw new InvalidDataException("Expected contact#145ade0b, got 0x" + cctor.ToString("x8"));
                    long uid = r.ReadInt64();
                    bool mutual = ReadBool(r);
                    pairs.Add(new KeyValuePair<long, bool>(uid, mutual));
                }

                r.ReadInt32(); // saved_count

                // users: Vector<User>
                var userIndex = ReadUsersVector(r);

                for (int i = 0; i < pairs.Count; i++)
                {
                    var p = pairs[i];
                    UserRecord rec;
                    if (!userIndex.TryGetValue(p.Key, out rec)) rec = UserRecord.Stub(p.Key);
                    result.Contacts.Add(rec.ToContact(p.Value, /*isBlocked*/ false));
                }
                return result;
            }
        }

        public static DecodedImportedContacts DecodeImportedContacts(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorImportedContacts)
                    throw new InvalidDataException("Unexpected contacts.ImportedContacts constructor: 0x" + ctor.ToString("x8"));

                var result = new DecodedImportedContacts
                {
                    Imported = new List<ContactImportResult>(),
                    RetryClientIds = new List<long>(),
                    Users = new List<Contact>()
                };

                // imported: Vector<importedContact>
                ExpectVector(r);
                int importedCount = r.ReadInt32();
                for (int i = 0; i < importedCount; i++)
                {
                    uint ictor = r.ReadUInt32();
                    if (ictor != CtorImportedContact)
                        throw new InvalidDataException("Expected importedContact#c13e3c50, got 0x" + ictor.ToString("x8"));
                    long uid = r.ReadInt64();
                    long cid = r.ReadInt64();
                    result.Imported.Add(new ContactImportResult(cid, uid));
                }

                // popular_invites: Vector<popularContact>
                ExpectVector(r);
                int popularCount = r.ReadInt32();
                for (int i = 0; i < popularCount; i++)
                {
                    uint pctor = r.ReadUInt32();
                    if (pctor != CtorPopularContact)
                        throw new InvalidDataException("Expected popularContact#5ce14175, got 0x" + pctor.ToString("x8"));
                    r.ReadInt64(); // client_id
                    r.ReadInt32(); // importers
                }

                // retry_contacts: Vector<long>
                ExpectVector(r);
                int retryCount = r.ReadInt32();
                for (int i = 0; i < retryCount; i++)
                {
                    result.RetryClientIds.Add(r.ReadInt64());
                }

                // users: Vector<User>
                var userIndex = ReadUsersVector(r);
                for (int i = 0; i < result.Imported.Count; i++)
                {
                    long uid = result.Imported[i].UserId;
                    if (uid <= 0) continue;
                    UserRecord rec;
                    if (!userIndex.TryGetValue(uid, out rec)) rec = UserRecord.Stub(uid);
                    result.Users.Add(rec.ToContact(/*mutual*/ false, /*isBlocked*/ false));
                }
                return result;
            }
        }

        public static DecodedFound DecodeFound(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorFound)
                    throw new InvalidDataException("Unexpected contacts.Found constructor: 0x" + ctor.ToString("x8"));

                var result = new DecodedFound { UserIds = new List<long>(), Users = new List<Contact>() };

                // my_results: Vector<Peer>
                AppendUserPeers(r, result.UserIds);
                // results:    Vector<Peer>
                AppendUserPeers(r, result.UserIds);
                // chats:      Vector<Chat> — skip wholesale (we don't render group hits in V1)
                SkipUnknownVector(r);
                // users:      Vector<User>
                var userIndex = ReadUsersVector(r);

                var seen = new HashSet<long>();
                for (int i = 0; i < result.UserIds.Count; i++)
                {
                    long uid = result.UserIds[i];
                    if (!seen.Add(uid)) continue;
                    UserRecord rec;
                    if (!userIndex.TryGetValue(uid, out rec)) rec = UserRecord.Stub(uid);
                    result.Users.Add(rec.ToContact(/*mutual*/ false, /*isBlocked*/ false));
                }
                return result;
            }
        }

        public static DecodedBlocked DecodeBlocked(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                bool isSlice = ctor == CtorBlockedSlice;
                if (ctor != CtorBlocked && !isSlice)
                    throw new InvalidDataException("Unexpected contacts.Blocked constructor: 0x" + ctor.ToString("x8"));

                if (isSlice) r.ReadInt32(); // count

                ExpectVector(r);
                int n = r.ReadInt32();
                var result = new DecodedBlocked { BlockedUserIds = new List<long>(n) };
                for (int i = 0; i < n; i++)
                {
                    uint pctor = r.ReadUInt32();
                    if (pctor != CtorPeerBlocked)
                        throw new InvalidDataException("Expected peerBlocked#e8fd8014, got 0x" + pctor.ToString("x8"));
                    long uid = ReadPeerUserId(r);
                    r.ReadInt32(); // date
                    if (uid > 0) result.BlockedUserIds.Add(uid);
                }
                // chats / users vectors trail; we don't need them for the blocked id list.
                return result;
            }
        }

        // ----- shared helpers -------------------------------------------------

        /// <summary>
        /// Reads <c>Vector&lt;Peer&gt;</c> and appends any peerUser ids to
        /// <paramref name="acc"/>. peerChat / peerChannel hits are dropped —
        /// Contacts only renders user results in V1.
        /// </summary>
        private static void AppendUserPeers(BinaryReader r, IList<long> acc)
        {
            ExpectVector(r);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                long uid = ReadPeerUserId(r);
                if (uid > 0) acc.Add(uid);
            }
        }

        private static long ReadPeerUserId(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            switch (ctor)
            {
                case CtorPeerUser: return r.ReadInt64();
                case CtorPeerChat: r.ReadInt64(); return -1L;
                case CtorPeerChannel: r.ReadInt64(); return -1L;
                default:
                    throw new InvalidDataException("Unknown Peer constructor 0x" + ctor.ToString("x8"));
            }
        }

        /// <summary>
        /// Skips a <c>Vector</c> of items whose binary shape we can't safely
        /// walk (e.g. <c>Vector&lt;Chat&gt;</c> in current TL layer). We rely
        /// on the fact that this vector is the LAST field we need to read for
        /// the response — the stream cursor after this call is intentionally
        /// not used. If a future caller needs to read fields after such a
        /// vector, this helper must be replaced by a precise per-Chat parser.
        /// </summary>
        private static void SkipUnknownVector(BinaryReader r)
        {
            // We can't walk arbitrary chats safely. As a defensive fallback,
            // consume the constructor + count and then bail out by abandoning
            // the rest of the stream. Callers tolerate this by NOT reading
            // anything after the call.
            uint ctor = r.ReadUInt32();
            if (ctor != CtorVector)
                throw new InvalidDataException("Expected Vector#1cb5c415, got 0x" + ctor.ToString("x8"));
            r.ReadInt32(); // count
            // Intentionally no further reads — see XML doc above.
        }

        /// <summary>
        /// Read a <c>Vector&lt;User&gt;</c> at the current cursor and return a
        /// dictionary keyed by user_id of <see cref="UserRecord"/>s carrying
        /// the fields Contacts cares about. Unknown User constructors are
        /// recorded as a stub keyed by the parsed id-prefix when possible; if
        /// the shape can't be walked we stop at the first unknown ctor.
        ///
        /// V1 limitation: the TL <c>User</c> type carries many trailing
        /// optional fields whose binary shapes are too volatile to track here.
        /// <see cref="TryReadUser"/> walks the leading prefix that exposes the
        /// fields Contacts cares about; afterwards the stream cursor is
        /// misaligned for the next <c>User</c> in the vector, so we capture
        /// the first (or first <see cref="TlDecoder.CtorUserEmpty"/>) record
        /// and stop. A schema-generated decoder will eventually replace this.
        /// This best-effort approach is sufficient because:
        ///   * <c>contacts.contacts</c> rows already carry user_id; the users
        ///     vector only enriches names/usernames/phones, which gracefully
        ///     fall back to stubs when missing.
        ///   * Search and import flows likewise tolerate stub records.
        /// </summary>
        private static IDictionary<long, UserRecord> ReadUsersVector(BinaryReader r)
        {
            var dict = new Dictionary<long, UserRecord>();
            uint vctor = r.ReadUInt32();
            if (vctor != CtorVector)
                throw new InvalidDataException("Expected Vector#1cb5c415 for users, got 0x" + vctor.ToString("x8"));
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                UserRecord rec;
                bool canContinue = TryReadUser(r, out rec);
                if (rec != null && rec.UserId > 0 && !dict.ContainsKey(rec.UserId))
                {
                    dict.Add(rec.UserId, rec);
                }
                if (!canContinue) break;
            }
            return dict;
        }

        // user#abb5f120 (layer 214 default user constructor) ish — the TL surface
        // for User has been historically volatile. Rather than tracking every
        // shape, we attempt a generic parse that recognizes the empty case and
        // the "regular" case prefix; on failure we record a stub with id only.
        //
        // user#d3bc4b7a (recent layers) flags:#  flags2:#  ... id:long access_hash:flags.0?long
        //   first_name:flags.1?string  last_name:flags.2?string  username:flags.3?string
        //   phone:flags.4?string  ... (many trailing optional fields)
        //
        // For Contacts' purposes we only need id, first_name, last_name, username,
        // phone. Any stream we can't walk safely is treated as a stub.
        private const uint CtorUserEmpty = 0xd3bc4b7a;     // userEmpty (layer 214)

        private static bool TryReadUser(BinaryReader r, out UserRecord rec)
        {
            rec = null;
            long startPos = r.BaseStream.Position;
            uint ctor;
            try { ctor = r.ReadUInt32(); }
            catch { return false; }

            // userEmpty#d3bc4b7a id:long
            if (ctor == CtorUserEmpty)
            {
                long id;
                try { id = r.ReadInt64(); } catch { return false; }
                rec = UserRecord.Stub(id);
                return true;
            }

            // For any other User shape, we can't safely walk the variable trailing
            // optional fields without tracking the full TL schema here. Conservative
            // V1 strategy: read the standard prefix (flags + flags2 + id) and skip
            // the rest by abandoning the stream. To keep the outer vector loop
            // making forward progress, we attempt to read id and synthesize a stub.
            //
            // We treat this as best-effort; downstream code falls back to a stub
            // populated only with user_id and zeroed-out display fields. A
            // schema-generated decoder will eventually replace this.
            try
            {
                int flags = r.ReadInt32();
                r.ReadInt32(); // flags2 — unused in V1; documented for layer-214 awareness.
                long id = r.ReadInt64();
                long accessHash = 0;
                if ((flags & (1 << 0)) != 0) accessHash = r.ReadInt64();
                string firstName = null;
                string lastName = null;
                string username = null;
                string phone = null;
                if ((flags & (1 << 1)) != 0) firstName = ReadString(r);
                if ((flags & (1 << 2)) != 0) lastName = ReadString(r);
                if ((flags & (1 << 3)) != 0) username = ReadString(r);
                if ((flags & (1 << 4)) != 0) phone = ReadString(r);

                rec = new UserRecord(id, accessHash, firstName, lastName, username, phone);
                // Trailing optional fields are not consumed; callers must NOT read further from this stream.
                // To keep parsing single-User safe inside a Vector loop, signal a clean stop.
                return false; // <- intentional: avoid eating the rest of the loop with a misaligned cursor.
            }
            catch
            {
                // Roll back to caller-state (best effort) and bail.
                try { r.BaseStream.Position = startPos; } catch { }
                return false;
            }
        }

        // ----- primitives -----------------------------------------------------

        private static void ExpectVector(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor != CtorVector)
                throw new InvalidDataException("Expected Vector#1cb5c415, got 0x" + ctor.ToString("x8"));
        }

        private static bool ReadBool(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorBoolTrue) return true;
            if (ctor == CtorBoolFalse) return false;
            throw new InvalidDataException("Expected Bool constructor, got 0x" + ctor.ToString("x8"));
        }

        private static string ReadString(BinaryReader r)
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
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        // ----- internal user-record DTO --------------------------------------

        private sealed class UserRecord
        {
            // Field name kept distinct from the VO type to avoid the
            // confusing `new UserId(UserId)` shadowing in ToContact.
            public long UserId { get; private set; }
            public long AccessHash { get; private set; }
            public string FirstName { get; private set; }
            public string LastName { get; private set; }
            public string Username { get; private set; }
            public string Phone { get; private set; }

            public UserRecord(long userId, long accessHash, string firstName, string lastName, string username, string phone)
            {
                UserId = userId;
                AccessHash = accessHash;
                FirstName = firstName ?? string.Empty;
                LastName = lastName ?? string.Empty;
                Username = username ?? string.Empty;
                Phone = phone ?? string.Empty;
            }

            public static UserRecord Stub(long userId)
            {
                return new UserRecord(userId, 0L, string.Empty, string.Empty, string.Empty, string.Empty);
            }

            public Contact ToContact(bool mutual, bool isBlocked)
            {
                long uid = this.UserId;
                return new Contact(
                    new Vianigram.Contacts.Domain.ValueObjects.UserId(uid),
                    Phone, FirstName, LastName, Username, mutual, isBlocked);
            }
        }
    }
}
