// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Search.Domain.Entities;

namespace Vianigram.Search.Application.UseCases
{
    /// <summary>
    /// Cancel an in-flight session. Idempotent. The handler transitions the
    /// session to <c>Cancelled</c>, stages a <c>SearchCancelled</c> event, and
    /// drains the bus.
    ///
    /// <para>V1 does not propagate the cancellation to the underlying MTProto
    /// request in flight — Telegram does not support per-RPC cancel; the next
    /// response is simply ignored by the application layer because the
    /// session is already terminal.</para>
    /// </summary>
    public sealed class CancelSearchCommand
    {
        public SearchSession Session { get; private set; }
        public string Reason { get; private set; }

        public CancelSearchCommand(SearchSession session, string reason = "user")
        {
            if (session == null) throw new ArgumentNullException("session");
            Session = session;
            Reason = string.IsNullOrEmpty(reason) ? "user" : reason;
        }
    }
}
