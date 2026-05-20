// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Search.Domain.Entities;

namespace Vianigram.Search.Application.UseCases
{
    /// <summary>
    /// Continue a paged session with its current cursor. The handler reads the
    /// cursor + query from the supplied session, issues the appropriate RPC
    /// (the same one that started the session), and appends the new page to
    /// the aggregate.
    ///
    /// Calling on a terminal session (Completed / Cancelled / Failed) is a
    /// no-op success; the handler returns the unchanged session.
    /// </summary>
    public sealed class LoadMoreCommand
    {
        public SearchSession Session { get; private set; }

        public LoadMoreCommand(SearchSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            Session = session;
        }
    }
}
