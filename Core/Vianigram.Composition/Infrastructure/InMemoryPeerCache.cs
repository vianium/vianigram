// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// InMemoryPeerCache.cs
// Process-local <see cref="IPeerCache"/> implementation.
//
// Concurrency: ConcurrentDictionary<long,*> for every map — reads and
// writes are lock-free. Bulk slice updates iterate the input list and call
// the per-key setter; we accept a tiny race when two independent RPCs
// return overlapping users/chats (last-writer-wins, both servers agree on
// the access_hash and display name so the eventual value is identical).

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Default <see cref="IPeerCache"/>: process-local, in-memory,
    /// thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// </summary>
    public sealed class InMemoryPeerCache : IPeerCache
    {
        private readonly ConcurrentDictionary<long, long> _users = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, long> _channels = new ConcurrentDictionary<long, long>();
        private readonly ConcurrentDictionary<long, string> _userNames = new ConcurrentDictionary<long, string>();
        private readonly ConcurrentDictionary<long, string> _chatTitles = new ConcurrentDictionary<long, string>();
        private readonly ConcurrentDictionary<int, MessagePreview> _messagePreviews = new ConcurrentDictionary<int, MessagePreview>();
        // Inline-thumbnail bytes per peer. ~80 bytes each, capped by total
        // user/chat count observed during this session — bounded by
        // Telegram's typical active-dialog volume (~few hundred). No eviction.
        private readonly ConcurrentDictionary<long, byte[]> _userPhotosStripped = new ConcurrentDictionary<long, byte[]>();
        private readonly ConcurrentDictionary<long, byte[]> _chatPhotosStripped = new ConcurrentDictionary<long, byte[]>();
        // (photoId, dcId) refs for the HD avatar download. Pair stored as a
        // single tuple to keep the two values consistent (no risk of
        // fetching photoId on the wrong DC).
        private readonly ConcurrentDictionary<long, PhotoRef> _userPhotoRefs = new ConcurrentDictionary<long, PhotoRef>();
        private readonly ConcurrentDictionary<long, PhotoRef> _chatPhotoRefs = new ConcurrentDictionary<long, PhotoRef>();

        private struct PhotoRef
        {
            public long PhotoId;
            public int DcId;
        }

        private struct MessagePreview
        {
            public string Text;
            public System.DateTime DateUtc;
        }

        public long? GetUserAccessHash(long userId)
        {
            long v;
            if (_users.TryGetValue(userId, out v)) return v;
            return null;
        }

        public long? GetChannelAccessHash(long channelId)
        {
            long v;
            if (_channels.TryGetValue(channelId, out v)) return v;
            return null;
        }

        public void SetUserAccessHash(long userId, long accessHash)
        {
            // We cache even access_hash=0 because the server's userEmpty /
            // user records with bit-0 cleared are valid "no access_hash
            // needed" peers. Skipping zero would make us re-query every
            // time. Caller responsibility: don't insert garbage ids.
            _users[userId] = accessHash;
        }

        public void SetChannelAccessHash(long channelId, long accessHash)
        {
            _channels[channelId] = accessHash;
        }

        public void UpdateFromUsersSlice(IList<RawUser> users)
        {
            if (users == null) return;
            for (int i = 0; i < users.Count; i++)
            {
                RawUser u = users[i];
                if (u == null) continue;
                if (u.Id == 0) continue;
                _users[u.Id] = u.AccessHash;

                // Also stash a display name. We only overwrite when the
                // incoming record actually carries one — repeated slices
                // sometimes drop the name fields (min-user records) and
                // we don't want to clobber a previously-seen full name.
                string display = ComposeUserDisplayName(u);
                if (!string.IsNullOrEmpty(display))
                {
                    _userNames[u.Id] = display;
                }

                // Cache the inline thumbnail when the slice carried one.
                // Same write policy as the display name — never overwrite a
                // previously-cached photo with null (min-user slices often
                // drop it).
                if (u.StrippedPhoto != null && u.StrippedPhoto.Length >= 3)
                {
                    _userPhotosStripped[u.Id] = u.StrippedPhoto;
                }
                // Photo ref for HD fetch.
                if (u.PhotoId != 0L && u.PhotoDcId > 0)
                {
                    _userPhotoRefs[u.Id] = new PhotoRef { PhotoId = u.PhotoId, DcId = u.PhotoDcId };
                }
            }
        }

        public void UpdateFromChatsSlice(IList<RawChat> chats)
        {
            if (chats == null) return;
            for (int i = 0; i < chats.Count; i++)
            {
                RawChat c = chats[i];
                if (c == null) continue;
                if (c.Id == 0) continue;
                if (c.IsChannel)
                {
                    _channels[c.Id] = c.AccessHash;
                }
                // Basic chats (chat#41cbf256) don't need access_hash for
                // inputPeerChat — we silently skip the access map for them.
                // Their id alone is sufficient on the wire.

                if (!string.IsNullOrEmpty(c.Title))
                {
                    _chatTitles[c.Id] = c.Title;
                }

                if (c.StrippedPhoto != null && c.StrippedPhoto.Length >= 3)
                {
                    _chatPhotosStripped[c.Id] = c.StrippedPhoto;
                }
                if (c.PhotoId != 0L && c.PhotoDcId > 0)
                {
                    _chatPhotoRefs[c.Id] = new PhotoRef { PhotoId = c.PhotoId, DcId = c.PhotoDcId };
                }
            }
        }

        public int UserCount { get { return _users.Count; } }
        public int ChannelCount { get { return _channels.Count; } }

        public string GetUserDisplayName(long userId)
        {
            string v;
            if (_userNames.TryGetValue(userId, out v) && !string.IsNullOrEmpty(v)) return v;
            return string.Empty;
        }

        public string GetChatTitle(long chatOrChannelId)
        {
            string v;
            if (_chatTitles.TryGetValue(chatOrChannelId, out v) && !string.IsNullOrEmpty(v)) return v;
            return string.Empty;
        }

        public byte[] GetUserPhotoStripped(long userId)
        {
            byte[] v;
            return _userPhotosStripped.TryGetValue(userId, out v) ? v : null;
        }

        public byte[] GetChatPhotoStripped(long chatOrChannelId)
        {
            byte[] v;
            return _chatPhotosStripped.TryGetValue(chatOrChannelId, out v) ? v : null;
        }

        public bool TryGetUserPhotoRef(long userId, out long photoId, out int dcId)
        {
            PhotoRef r;
            if (_userPhotoRefs.TryGetValue(userId, out r))
            {
                photoId = r.PhotoId;
                dcId = r.DcId;
                return true;
            }
            photoId = 0L;
            dcId = 0;
            return false;
        }

        public bool TryGetChatPhotoRef(long chatOrChannelId, out long photoId, out int dcId)
        {
            PhotoRef r;
            if (_chatPhotoRefs.TryGetValue(chatOrChannelId, out r))
            {
                photoId = r.PhotoId;
                dcId = r.DcId;
                return true;
            }
            photoId = 0L;
            dcId = 0;
            return false;
        }

        public void SetMessagePreview(int messageId, string text, System.DateTime dateUtc)
        {
            if (messageId <= 0) return;
            _messagePreviews[messageId] = new MessagePreview
            {
                Text = text ?? string.Empty,
                DateUtc = dateUtc
            };
        }

        public bool TryGetMessagePreview(int messageId, out string text, out System.DateTime dateUtc)
        {
            MessagePreview p;
            if (_messagePreviews.TryGetValue(messageId, out p))
            {
                text = p.Text ?? string.Empty;
                dateUtc = p.DateUtc;
                return true;
            }
            text = string.Empty;
            dateUtc = default(System.DateTime);
            return false;
        }

        private static string ComposeUserDisplayName(RawUser u)
        {
            string first = u.FirstName ?? string.Empty;
            string last = u.LastName ?? string.Empty;
            string both = (first + " " + last).Trim();
            if (!string.IsNullOrEmpty(both)) return both;
            if (!string.IsNullOrEmpty(u.Username)) return "@" + u.Username;
            if (!string.IsNullOrEmpty(u.Phone)) return "+" + u.Phone;
            return string.Empty;
        }
    }
}
