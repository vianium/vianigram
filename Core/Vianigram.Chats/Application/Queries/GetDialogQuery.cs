// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Queries
{
    /// <summary>
    /// Read-side query for a single dialog aggregate by peer.
    /// Resolves to <c>Result&lt;Dialog, ChatError&gt;</c> at the handler.
    /// </summary>
    public sealed class GetDialogQuery
    {
        public PeerId Peer { get; private set; }

        public GetDialogQuery(PeerId peer)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
        }
    }
}
