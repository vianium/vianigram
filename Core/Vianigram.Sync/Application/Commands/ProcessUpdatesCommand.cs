// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Sync.Domain.ValueObjects;

namespace Vianigram.Sync.Application.Commands
{
    /// <summary>
    /// Apply a server-sent UpdatesEnvelope to SyncState. The ordinary hot path:
    /// the updates loop decodes a raw TL byte buffer into <see cref="Envelope"/>
    /// and dispatches this command for ordered, single-dispatcher application.
    /// </summary>
    public sealed class ProcessUpdatesCommand
    {
        public ProcessUpdatesCommand(UpdatesEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException("envelope");
            Envelope = envelope;
        }

        public UpdatesEnvelope Envelope { get; private set; }
    }
}
