// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Application.UseCases
{
    /// <summary>
    /// Push a new badge value to the platform sink and update the
    /// aggregate's stored count.
    /// </summary>
    public sealed class UpdateBadgeCommand
    {
        public BadgeCount Count { get; private set; }

        public UpdateBadgeCommand(BadgeCount count)
        {
            Count = count;
        }
    }
}
