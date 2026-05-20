// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Contacts.Domain.ValueObjects;

namespace Vianigram.Contacts.Application.UseCases
{
    /// <summary>
    /// Block a user via <c>contacts.block#2e2e8734</c>. We carry the access
    /// hash separately because <c>InputPeerUser</c> requires it; for users we
    /// already know about (in the contact book) the access hash is taken from
    /// there, but the inbound port lets callers also supply one explicitly.
    /// </summary>
    public sealed class BlockUserCommand
    {
        public UserId Target { get; private set; }
        public long AccessHash { get; private set; }

        public BlockUserCommand(UserId target, long accessHash)
        {
            if (target.Value <= 0) throw new ArgumentException("target must be positive", "target");
            Target = target;
            AccessHash = accessHash;
        }
    }
}
