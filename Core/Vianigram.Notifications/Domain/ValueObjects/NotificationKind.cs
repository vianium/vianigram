// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Notifications.Domain.ValueObjects
{
    /// <summary>
    /// Categories of notification surfaced by the bounded context. Drives toast
    /// formatting (icon / sound) and which mute predicates apply.
    ///
    /// Mirrors the categories tracked by TDLib's NotificationManager
    /// (<c>NotificationManager.cpp</c>): plain messages, mentions / replies,
    /// reactions on the user's messages, group membership changes, incoming
    /// calls. Each is independently mutable per chat.
    /// </summary>
    public enum NotificationKind
    {
        /// <summary>Plain incoming message (default category).</summary>
        Message = 0,
        /// <summary>The active account was @-mentioned or directly replied to.</summary>
        Mention = 1,
        /// <summary>A reaction was added to one of the active account's messages.</summary>
        Reaction = 2,
        /// <summary>The active account was added to a group / supergroup.</summary>
        GroupAdd = 3,
        /// <summary>Incoming call ringing.</summary>
        CallIncoming = 4
    }
}
