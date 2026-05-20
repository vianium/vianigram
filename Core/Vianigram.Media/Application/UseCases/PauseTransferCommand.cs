// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Application.UseCases
{
    public sealed class PauseTransferCommand
    {
        public PauseTransferCommand(MediaId id)
        {
            Id = id;
        }

        public MediaId Id { get; private set; }
    }
}
