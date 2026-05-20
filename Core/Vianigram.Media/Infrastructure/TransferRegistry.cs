// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Media.Domain.Entities;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Infrastructure
{
    /// <summary>
    /// Thread-safe in-memory registry of active <see cref="MediaTransfer"/>
    /// aggregates. Pause/Resume/Cancel handlers and the synchronous
    /// <see cref="Ports.Inbound.IMediaApi.GetTransfer"/> read-through both
    /// look up by <see cref="MediaId"/> here. A future revision may replace
    /// this with a SQLite-backed projection so transfers survive app launches.
    ///
    /// <para>Single global lock is acceptable at the current scale (≤ 8 active
    /// transfers). The hot path inside each transfer (chunk fan-out) does
    /// not contend with this lock; it operates on the aggregate after the
    /// lookup completes.</para>
    /// </summary>
    public sealed class TransferRegistry
    {
        private readonly Dictionary<MediaId, MediaTransfer> _byId;
        private readonly object _gate;

        public TransferRegistry()
        {
            _byId = new Dictionary<MediaId, MediaTransfer>();
            _gate = new object();
        }

        public void Add(MediaTransfer transfer)
        {
            if (transfer == null) return;
            lock (_gate)
            {
                _byId[transfer.Id] = transfer;
            }
        }

        public MediaTransfer Find(MediaId id)
        {
            MediaTransfer t;
            lock (_gate)
            {
                _byId.TryGetValue(id, out t);
            }
            return t;
        }

        public bool Remove(MediaId id)
        {
            lock (_gate)
            {
                return _byId.Remove(id);
            }
        }

        public int Count
        {
            get { lock (_gate) { return _byId.Count; } }
        }
    }
}
