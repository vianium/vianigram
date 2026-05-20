// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Media.Domain.ValueObjects
{
    /// <summary>
    /// Server-side handle to a downloadable file. Carries the union of fields
    /// across the four <c>inputFileLocation</c> shapes used by Telegram's
    /// <c>upload.getFile</c>:
    ///
    ///   inputPhotoFileLocation       (id, accessHash, fileReference, thumbSize)
    ///   inputDocumentFileLocation    (id, accessHash, fileReference, thumbSize)
    ///   inputPeerPhotoFileLocation   (peer, photoId)
    ///   inputFileLocation (legacy)   (volumeId, localId, secret, fileReference)
    ///
    /// We carry all of them as nullable fields and dispatch on <see cref="Kind"/>.
    /// The data center id is logically separate — Telegram's <c>upload.getFile</c>
    /// is invoked on a specific DC determined by the photo or document; we
    /// expose it here so the orchestrator routes calls correctly.
    /// </summary>
    public sealed class FileLocation : IEquatable<FileLocation>
    {
        private FileLocation(
            FileLocationKind kind,
            int dcId,
            long id,
            long accessHash,
            byte[] fileReference,
            long volumeId,
            int localId,
            long secret,
            string thumbSize,
            PeerPhotoKind peerKind,
            long peerId,
            long peerAccessHash,
            bool big)
        {
            Kind = kind;
            DcId = dcId;
            Id = id;
            AccessHash = accessHash;
            FileReference = fileReference ?? new byte[0];
            VolumeId = volumeId;
            LocalId = localId;
            Secret = secret;
            ThumbSize = thumbSize ?? string.Empty;
            PeerKind = peerKind;
            PeerId = peerId;
            PeerAccessHash = peerAccessHash;
            Big = big;
        }

        public FileLocationKind Kind { get; private set; }
        public int DcId { get; private set; }
        public long Id { get; private set; }
        public long AccessHash { get; private set; }
        public byte[] FileReference { get; private set; }
        public long VolumeId { get; private set; }
        public int LocalId { get; private set; }
        public long Secret { get; private set; }
        public string ThumbSize { get; private set; }

        // Peer-photo fields. Only populated when Kind = PeerPhoto. The
        // encoder writes inputPeerPhotoFileLocation#37257e99 with these values.
        public PeerPhotoKind PeerKind { get; private set; }
        public long PeerId { get; private set; }
        public long PeerAccessHash { get; private set; }
        public bool Big { get; private set; }

        public static FileLocation Document(int dcId, long id, long accessHash, byte[] fileReference, string thumbSize = "")
        {
            return new FileLocation(FileLocationKind.Document, dcId, id, accessHash, fileReference, 0, 0, 0, thumbSize,
                PeerPhotoKind.None, 0, 0, false);
        }

        public static FileLocation Photo(int dcId, long id, long accessHash, byte[] fileReference, string thumbSize)
        {
            return new FileLocation(FileLocationKind.Photo, dcId, id, accessHash, fileReference, 0, 0, 0, thumbSize,
                PeerPhotoKind.None, 0, 0, false);
        }

        public static FileLocation Legacy(int dcId, long volumeId, int localId, long secret, byte[] fileReference)
        {
            return new FileLocation(FileLocationKind.Legacy, dcId, 0, 0, fileReference, volumeId, localId, secret, string.Empty,
                PeerPhotoKind.None, 0, 0, false);
        }

        /// <summary>
        /// A Telegram peer's profile photo. The wire encoder serialises this as
        /// <c>inputPeerPhotoFileLocation#37257e99 flags:# big:flags.0?true peer:InputPeer photo_id:long</c>.
        /// <paramref name="big"/> selects the higher-resolution variant
        /// (640×640) versus the small one (160×160). For the chat
        /// list avatars we use small (false) — the row only needs a
        /// 48 px circle. Equality / hashing keys on <c>(PeerKind, PeerId, Id)</c>
        /// so the same avatar requested via DM-row vs group-member-row
        /// hits the same cache entry.
        /// </summary>
        public static FileLocation PeerPhoto(int dcId, PeerPhotoKind peerKind, long peerId, long peerAccessHash, long photoId, bool big)
        {
            return new FileLocation(FileLocationKind.PeerPhoto, dcId, photoId, 0, null, 0, 0, 0, string.Empty,
                peerKind, peerId, peerAccessHash, big);
        }

        public bool Equals(FileLocation other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (Kind != other.Kind) return false;
            if (DcId != other.DcId) return false;
            switch (Kind)
            {
                case FileLocationKind.Document:
                case FileLocationKind.Photo:
                    return Id == other.Id && AccessHash == other.AccessHash;
                case FileLocationKind.Legacy:
                    return VolumeId == other.VolumeId && LocalId == other.LocalId && Secret == other.Secret;
                case FileLocationKind.PeerPhoto:
                    return PeerKind == other.PeerKind
                        && PeerId == other.PeerId
                        && Id == other.Id
                        && Big == other.Big;
                default:
                    return false;
            }
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FileLocation);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind;
                h = (h * 397) ^ DcId;
                h = (h * 397) ^ (int)(Id ^ (Id >> 32));
                h = (h * 397) ^ (int)(AccessHash ^ (AccessHash >> 32));
                h = (h * 397) ^ (int)(VolumeId ^ (VolumeId >> 32));
                h = (h * 397) ^ LocalId;
                return h;
            }
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case FileLocationKind.Document:
                    return "Document(dc=" + DcId + ", id=" + Id + ")";
                case FileLocationKind.Photo:
                    return "Photo(dc=" + DcId + ", id=" + Id + ", thumb=" + ThumbSize + ")";
                case FileLocationKind.Legacy:
                    return "Legacy(dc=" + DcId + ", vol=" + VolumeId + ", local=" + LocalId + ")";
                case FileLocationKind.PeerPhoto:
                    return "PeerPhoto(dc=" + DcId + ", peer=" + PeerKind + ":" + PeerId +
                           ", id=" + Id + ", big=" + Big + ")";
                default:
                    return "FileLocation(unknown)";
            }
        }
    }

    public enum FileLocationKind
    {
        Unknown = 0,
        Document = 1,
        Photo = 2,
        Legacy = 3,
        // Peer profile photo.
        PeerPhoto = 4
    }

    /// <summary>
    /// Mirror of TL <c>InputPeer</c> sub-types relevant for peer-photo
    /// downloads. Channels include broadcast and megagroup variants —
    /// both serialise as <c>inputPeerChannel</c>.
    /// </summary>
    public enum PeerPhotoKind
    {
        None = 0,
        User = 1,
        Chat = 2,
        Channel = 3
    }
}
