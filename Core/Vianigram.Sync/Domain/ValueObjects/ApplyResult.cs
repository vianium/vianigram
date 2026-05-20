// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Kernel.Events;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Output of <see cref="Vianigram.Sync.Domain.Entities.SyncState"/>.Apply.
    ///
    /// Carries:
    /// - <see cref="Events"/>      — derived domain events to publish on IEventBus
    ///                               (in order; downstream contexts see them in the
    ///                               same order Sync ingested them).
    /// - <see cref="NeedsGetDifference"/> — common-box gap; caller must invoke
    ///                                       updates.getDifference.
    /// - <see cref="NeedsChannelDifference"/> — channel ids needing per-channel
    ///                                          getChannelDifference.
    /// - <see cref="NeedsReseed"/> — server signaled updatesTooLong. Drop everything,
    ///                                refetch state from scratch.
    /// </summary>
    public sealed class ApplyResult
    {
        public ApplyResult(
            IList<IDomainEvent> events,
            bool needsGetDifference,
            IList<long> needsChannelDifference,
            bool needsReseed)
        {
            Events = events ?? new List<IDomainEvent>(0);
            NeedsGetDifference = needsGetDifference;
            NeedsChannelDifference = needsChannelDifference ?? new List<long>(0);
            NeedsReseed = needsReseed;
        }

        public IList<IDomainEvent> Events { get; private set; }
        public bool NeedsGetDifference { get; private set; }
        public IList<long> NeedsChannelDifference { get; private set; }
        public bool NeedsReseed { get; private set; }

        public static ApplyResult Empty()
        {
            return new ApplyResult(new List<IDomainEvent>(0), false, new List<long>(0), false);
        }
    }
}
