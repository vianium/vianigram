// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IPeerCache.cs
// Port + raw DTOs for the per-(client,peer) access_hash cache.
//
// Background:
//   Telegram inputUser/inputChannel/inputPeerUser/inputPeerChannel carry a
//   (id, access_hash) pair. The access_hash is an opaque token returned by
//   the server the first time the peer is observed (e.g. via users.getUsers,
//   dialog list, message sender). Subsequent calls referencing that peer
//   must echo the access_hash back; without it the server returns
//   PEER_ID_INVALID.
//
//   This cache lives at the adapter layer: handlers stay agnostic. Every
//   typed RPC response that carries users:Vector<User> / chats:Vector<Chat>
//   slices hydrates the cache, and every outbound input{User,Channel,Peer*}
//   builder consults the cache before falling back to access_hash 0.

using System.Collections.Generic;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Shared cache of per-peer access_hash tokens. Populated by handlers
    /// when responses include user/chat slices; consumed by handlers
    /// before issuing RPCs that require InputUser/InputChannel.
    /// Thread-safe.
    /// </summary>
    public interface IPeerCache
    {
        /// <summary>Returns the cached access_hash for the user, or null if unknown.</summary>
        long? GetUserAccessHash(long userId);

        /// <summary>Returns the cached access_hash for the channel, or null if unknown.</summary>
        long? GetChannelAccessHash(long channelId);

        void SetUserAccessHash(long userId, long accessHash);
        void SetChannelAccessHash(long channelId, long accessHash);

        /// <summary>Bulk-update from a TL <c>Vector&lt;User&gt;</c> slice (called on every RPC response that returns users).</summary>
        void UpdateFromUsersSlice(IList<RawUser> users);
        void UpdateFromChatsSlice(IList<RawChat> chats);

        /// <summary>Snapshot count, for diagnostics.</summary>
        int UserCount { get; }
        int ChannelCount { get; }

        /// <summary>
        /// Returns a display name for the user — typically "First Last" (or
        /// just "First" / username / phone fallback). Empty string if the
        /// user id has not been observed yet.
        /// </summary>
        string GetUserDisplayName(long userId);

        /// <summary>
        /// Returns the chat / channel title. Empty string if the id has not
        /// been observed yet.
        /// </summary>
        string GetChatTitle(long chatOrChannelId);

        /// <summary>
        /// Inline-thumbnail bytes (the "stripped" JPEG) for the user's
        /// profile photo. Null when the user has no photo or the slice
        /// hasn't surfaced it yet. The caller (UI layer) feeds this through
        /// the JPEG expander and hands the resulting BitmapImage to
        /// AvatarCircle.
        /// </summary>
        byte[] GetUserPhotoStripped(long userId);

        /// <summary>
        /// Same as <see cref="GetUserPhotoStripped"/> for chats / channels.
        /// </summary>
        byte[] GetChatPhotoStripped(long chatOrChannelId);

        /// <summary>
        /// The user's profile photo reference — <c>photoId</c> + storage
        /// <c>dcId</c>. Both zero means the user has no photo (UI falls
        /// back to initials). Used to construct an
        /// <c>inputPeerPhotoFileLocation</c> for the HD avatar download.
        /// </summary>
        bool TryGetUserPhotoRef(long userId, out long photoId, out int dcId);

        /// <summary>
        /// Same as <see cref="TryGetUserPhotoRef"/> for chats / channels.
        /// </summary>
        bool TryGetChatPhotoRef(long chatOrChannelId, out long photoId, out int dcId);

        /// <summary>
        /// Stash a last-message preview keyed by message id. Used by the
        /// dialog list to show "last activity" text and timestamp for each
        /// row. The same id may be set multiple times — last writer wins.
        /// </summary>
        void SetMessagePreview(int messageId, string text, System.DateTime dateUtc);

        /// <summary>
        /// Returns true and fills <paramref name="text"/> /
        /// <paramref name="dateUtc"/> with the cached preview; false if no
        /// preview is known for the id yet.
        /// </summary>
        bool TryGetMessagePreview(int messageId, out string text, out System.DateTime dateUtc);
    }

    /// <summary>
    /// Wire-shape mirror of the relevant subset of TL <c>user#83314fca</c> /
    /// <c>user#83314fae</c>. Plain fields (not properties) — this is a tiny
    /// transport DTO between the partial decoders and <see cref="IPeerCache"/>.
    /// </summary>
    public sealed class RawUser
    {
        public long Id;
        public long AccessHash;
        public string FirstName;
        public string LastName;
        public string Username;
        public string Phone;
        // Inline thumbnail (~80 bytes of stripped JPEG) extracted from the
        // photo:UserProfilePhoto field. Null when the user has no photo or
        // the slice didn't expose it. The expander in Vianigram.App.Services
        // pads this back to a full JPEG for BitmapImage.
        public byte[] StrippedPhoto;
        // Identifiers needed to issue upload.getFile with
        // inputPeerPhotoFileLocation. PhotoId == 0 indicates "no profile
        // photo" (the user is using initials); DcId is the storage DC where
        // the bytes live (often != main).
        public long PhotoId;
        public int PhotoDcId;
    }

    /// <summary>
    /// Wire-shape mirror of TL <c>chat#41cbf256</c> /
    /// <c>channel#1981ea7e</c> / <c>channelForbidden#17d493d5</c>. Basic
    /// chats use AccessHash=0 (correct — they don't require it for
    /// inputPeerChat).
    /// </summary>
    public sealed class RawChat
    {
        public long Id;
        public long AccessHash;
        public string Title;
        public bool IsChannel;
        public bool IsMegagroup;
        // Inline thumbnail mirror of RawUser.StrippedPhoto. Sourced from
        // photo:ChatPhoto on the chat / channel slice.
        public byte[] StrippedPhoto;
        // Photo identifiers for upload.getFile. See RawUser.PhotoId / PhotoDcId.
        public long PhotoId;
        public int PhotoDcId;
    }
}
