// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Application.UseCases
{
    /// <summary>
    /// Mark a peer's pending notifications as read. Clears the platform
    /// surfaces tied to the peer (toast / tile entries) and updates the
    /// badge to <see cref="NewBadge"/>. Does NOT modify the mute rule.
    /// </summary>
    public sealed class MarkAsReadCommand
    {
        public string PeerKey { get; private set; }
        public BadgeCount NewBadge { get; private set; }

        public MarkAsReadCommand(string peerKey, BadgeCount newBadge)
        {
            PeerKey = peerKey;
            NewBadge = newBadge;
        }
    }
}
