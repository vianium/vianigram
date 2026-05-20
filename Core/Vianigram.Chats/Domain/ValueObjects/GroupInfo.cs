// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GroupInfo.cs — Vianigram.Chats.Domain.ValueObjects
// Aggregated group/channel metadata returned by GetGroupInfoAsync.

using System;
using System.Collections.Generic;

namespace Vianigram.Chats.Domain.ValueObjects
{
    /// <summary>
    /// Snapshot of a group / supergroup / channel as returned by
    /// <c>messages.getFullChat</c> or <c>channels.getFullChannel</c>.
    /// Immutable value object — surface for the GroupInfoPageViewModel.
    ///
    /// Members may be a partial roster (the server caps the participants list
    /// for very large peers); callers should treat <see cref="MemberCount"/>
    /// as authoritative for size and <see cref="Members"/> as an opportunistic
    /// preview that may need lazy-loading for large groups.
    /// </summary>
    public sealed class GroupInfo
    {
        private readonly PeerId _peer;
        private readonly string _title;
        private readonly string _description;
        private readonly int _memberCount;
        private readonly IList<GroupMember> _members;
        private readonly bool _isAdmin;
        private readonly bool _isCreator;
        private readonly DateTimeOffset _createdAt;

        public GroupInfo(
            PeerId peer,
            string title,
            string description,
            int memberCount,
            IList<GroupMember> members,
            bool isAdmin,
            bool isCreator,
            DateTimeOffset createdAt)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            if (memberCount < 0) throw new ArgumentOutOfRangeException("memberCount");
            _peer = peer;
            _title = title ?? string.Empty;
            _description = description ?? string.Empty;
            _memberCount = memberCount;
            _members = members ?? new GroupMember[0];
            _isAdmin = isAdmin;
            _isCreator = isCreator;
            _createdAt = createdAt;
        }

        public PeerId Peer { get { return _peer; } }
        public string Title { get { return _title; } }
        public string Description { get { return _description; } }
        public int MemberCount { get { return _memberCount; } }
        public IList<GroupMember> Members { get { return _members; } }
        public bool IsAdmin { get { return _isAdmin; } }
        public bool IsCreator { get { return _isCreator; } }
        public DateTimeOffset CreatedAt { get { return _createdAt; } }
    }

    /// <summary>
    /// One participant of a <see cref="GroupInfo"/>. UserId / DisplayName /
    /// admin flag / join timestamp. Immutable.
    /// </summary>
    public sealed class GroupMember
    {
        private readonly long _userId;
        private readonly string _displayName;
        private readonly bool _isAdmin;
        private readonly DateTimeOffset _joinedAt;

        public GroupMember(long userId, string displayName, bool isAdmin, DateTimeOffset joinedAt)
        {
            if (userId == 0) throw new ArgumentException("userId must be non-zero", "userId");
            _userId = userId;
            _displayName = displayName ?? string.Empty;
            _isAdmin = isAdmin;
            _joinedAt = joinedAt;
        }

        public long UserId { get { return _userId; } }
        public string DisplayName { get { return _displayName; } }
        public bool IsAdmin { get { return _isAdmin; } }
        public DateTimeOffset JoinedAt { get { return _joinedAt; } }
    }
}
