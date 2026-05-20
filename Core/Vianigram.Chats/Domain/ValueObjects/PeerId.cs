// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Chats.Domain.ValueObjects
{
    /// <summary>
    /// Discriminator for the kind of peer addressed by a <see cref="PeerId"/>.
    /// Mirrors Telegram's three peer-shape namespaces (User / Chat / Channel)
    /// without leaking any TL constructor detail into the managed domain.
    /// </summary>
    public enum PeerKind
    {
        User = 0,
        Chat = 1,
        Channel = 2
    }

    /// <summary>
    /// Tagged identifier for a Telegram peer.
    ///
    /// PeerKind discriminates among User (1:1), Chat (basic group), and Channel
    /// (broadcast / megagroup). AccessHash is required for User and Channel to
    /// address the peer across DCs; Chat (basic group) carries access_hash = 0.
    ///
    /// Immutable value object: equality is by (Kind, Id).
    /// AccessHash is intentionally NOT part of equality — it is a routing token
    /// that may be refreshed by the server while the identity stays the same.
    /// </summary>
    public sealed class PeerId : IEquatable<PeerId>
    {
        private readonly PeerKind _kind;
        private readonly long _id;
        private readonly long _accessHash;

        private PeerId(PeerKind kind, long id, long accessHash)
        {
            _kind = kind;
            _id = id;
            _accessHash = accessHash;
        }

        public PeerKind Kind { get { return _kind; } }
        public long Id { get { return _id; } }
        public long AccessHash { get { return _accessHash; } }

        public static PeerId User(long userId, long accessHash)
        {
            if (userId <= 0) throw new ArgumentException("userId must be positive", "userId");
            return new PeerId(PeerKind.User, userId, accessHash);
        }

        public static PeerId Chat(long chatId)
        {
            if (chatId <= 0) throw new ArgumentException("chatId must be positive", "chatId");
            return new PeerId(PeerKind.Chat, chatId, 0L);
        }

        public static PeerId Channel(long channelId, long accessHash)
        {
            if (channelId <= 0) throw new ArgumentException("channelId must be positive", "channelId");
            return new PeerId(PeerKind.Channel, channelId, accessHash);
        }

        public bool Equals(PeerId other)
        {
            if (ReferenceEquals(other, null)) return false;
            return _kind == other._kind && _id == other._id;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PeerId);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (int)_kind;
                hash = (hash * 31) + _id.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return _kind.ToString().ToLowerInvariant() + ":" + _id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool operator ==(PeerId a, PeerId b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
            return a.Equals(b);
        }

        public static bool operator !=(PeerId a, PeerId b)
        {
            return !(a == b);
        }
    }
}
