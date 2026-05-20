// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Application.UseCases
{
    /// <summary>
    /// Synchronous query for a transfer's current state and progress. The
    /// handler is a pure read against the in-memory transfer registry — no
    /// I/O, no awaits — so callers can invoke it from UI-thread render paths.
    /// </summary>
    public sealed class GetTransferStateCommand
    {
        public GetTransferStateCommand(MediaId id)
        {
            Id = id;
        }

        public MediaId Id { get; private set; }
    }
}
