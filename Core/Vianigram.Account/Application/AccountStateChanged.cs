// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.ValueObjects;

namespace Vianigram.Account.Application
{
    /// <summary>
    /// EventArgs surfaced through <c>IAccountApi.StateChanged</c> so non-event-bus
    /// consumers (typical XAML view-models) can wire up a single subscription
    /// without taking a dependency on every domain event type.
    /// </summary>
    public sealed class AccountStateChanged : EventArgs
    {
        public AccountStateSnapshot Snapshot { get; private set; }

        public AccountStateChanged(AccountStateSnapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }
}
