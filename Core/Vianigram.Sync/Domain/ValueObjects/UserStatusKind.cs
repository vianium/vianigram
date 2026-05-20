// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Coarse-grained user presence state, abstracted from the TL constructor zoo
    /// (userStatusOnline, userStatusOffline, userStatusRecently, userStatusLastWeek,
    /// userStatusLastMonth, userStatusEmpty). Sync flattens those constructors to
    /// this enum so downstream contexts don't depend on TL details.
    /// </summary>
    public enum UserStatusKind
    {
        Empty = 0,
        Online = 1,
        Offline = 2,
        Recently = 3,
        LastWeek = 4,
        LastMonth = 5
    }
}
