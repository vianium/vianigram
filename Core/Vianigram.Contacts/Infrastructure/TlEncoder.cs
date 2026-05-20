// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Contacts.Domain.ValueObjects;

namespace Vianigram.Contacts.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the six RPC shapes Contacts issues.
    /// Mirrors the per-context approach used in <c>Vianigram.Chats</c> — Contacts
    /// only needs:
    ///
    ///   * contacts.getContacts#5dd69e12
    ///   * contacts.importContacts#2c800be5
    ///   * contacts.search#11f812d8
    ///   * contacts.block#2e2e8734
    ///   * contacts.unblock#b550d328
    ///   * contacts.getBlocked#9a868f80
    ///
    /// Plus the helper TL constructors used inline:
    ///   - inputPhoneContact#f392b7f4 (rows in importContacts)
    ///   - inputPeerUser#dde8a54c (block/unblock target)
    ///   - vector#1cb5c415 (Vector wrapper)
    ///
    /// All multi-byte integers are little-endian (TL convention). Strings use
    /// the standard TL byte-string framing (1- or 4-byte length prefix +
    /// padding to 4-byte alignment).
    /// </summary>
    internal static class TlEncoder
    {
        // ---- TL constructor ids ----------------------------------------------
        public const uint CtorGetContacts = 0x5dd69e12;
        public const uint CtorImportContacts = 0x2c800be5;
        public const uint CtorSearch = 0x11f812d8;
        public const uint CtorBlock = 0x2e2e8734;
        public const uint CtorUnblock = 0xb550d328;
        public const uint CtorGetBlocked = 0x9a868f80;

        public const uint CtorInputPhoneContact = 0xf392b7f4;
        public const uint CtorInputPeerEmpty = 0x7f3b18ea;
        public const uint CtorInputPeerUser = 0xdde8a54c;
        public const uint CtorVector = 0x1cb5c415;
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        // -------------------------------------------------------------------------
        // contacts.getContacts#5dd69e12  hash:long
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetContacts(long hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetContacts);
                w.Write(hash);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // contacts.importContacts#2c800be5  contacts:Vector<InputContact>
        //   inputPhoneContact#f392b7f4 client_id:long phone:string first_name:string last_name:string
        // -------------------------------------------------------------------------
        public static byte[] EncodeImportContacts(IList<ContactImportRequest> requests)
        {
            if (requests == null) throw new ArgumentNullException("requests");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorImportContacts);
                w.Write(CtorVector);
                w.Write(requests.Count);
                for (int i = 0; i < requests.Count; i++)
                {
                    var r = requests[i];
                    if (r == null) throw new ArgumentException("null import request at index " + i, "requests");
                    w.Write(CtorInputPhoneContact);
                    w.Write(r.ClientId);
                    WriteString(w, r.Phone == null ? string.Empty : r.Phone.Value);
                    WriteString(w, r.FirstName ?? string.Empty);
                    WriteString(w, r.LastName ?? string.Empty);
                }
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // contacts.search#11f812d8  q:string limit:int
        // -------------------------------------------------------------------------
        public static byte[] EncodeSearch(string query, int limit)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorSearch);
                WriteString(w, query ?? string.Empty);
                w.Write(limit);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // contacts.block#2e2e8734  flags:# my_stories_from:flags.0?true id:InputPeer
        // contacts.unblock#b550d328  flags:# my_stories_from:flags.0?true id:InputPeer
        // -------------------------------------------------------------------------
        public static byte[] EncodeBlock(long userId, long accessHash, bool myStoriesFrom)
        {
            return EncodeBlockOrUnblock(CtorBlock, userId, accessHash, myStoriesFrom);
        }

        public static byte[] EncodeUnblock(long userId, long accessHash, bool myStoriesFrom)
        {
            return EncodeBlockOrUnblock(CtorUnblock, userId, accessHash, myStoriesFrom);
        }

        private static byte[] EncodeBlockOrUnblock(uint ctor, long userId, long accessHash, bool myStoriesFrom)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(ctor);
                int flags = 0;
                if (myStoriesFrom) flags |= 1 << 0;
                w.Write(flags);

                // InputPeer: we expect a user (block/unblock semantics target users only).
                if (userId <= 0)
                {
                    w.Write(CtorInputPeerEmpty);
                }
                else
                {
                    w.Write(CtorInputPeerUser);
                    w.Write(userId);
                    w.Write(accessHash);
                }
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // contacts.getBlocked#9a868f80
        //   flags:#  my_stories_from:flags.0?true  offset:int  limit:int
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetBlocked(int offset, int limit, bool myStoriesFrom)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetBlocked);
                int flags = 0;
                if (myStoriesFrom) flags |= 1 << 0;
                w.Write(flags);
                w.Write(offset);
                w.Write(limit);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // TL string encoding: 1 length byte + bytes + padding to 4-byte align
        // (or, for length >= 254, 0xFE + 3 length bytes + bytes + padding).
        // -------------------------------------------------------------------------
        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? new byte[0] : Encoding.UTF8.GetBytes(s);
            int len = bytes.Length;
            int padding;
            if (len < 254)
            {
                w.Write((byte)len);
                w.Write(bytes);
                padding = (4 - ((len + 1) % 4)) % 4;
            }
            else
            {
                w.Write((byte)254);
                w.Write((byte)(len & 0xFF));
                w.Write((byte)((len >> 8) & 0xFF));
                w.Write((byte)((len >> 16) & 0xFF));
                w.Write(bytes);
                padding = (4 - (len % 4)) % 4;
            }
            for (int i = 0; i < padding; i++) w.Write((byte)0);
        }
    }
}
