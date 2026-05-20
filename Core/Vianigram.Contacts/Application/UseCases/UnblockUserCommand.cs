// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Contacts.Domain.ValueObjects;

namespace Vianigram.Contacts.Application.UseCases
{
    /// <summary>Symmetric counterpart to <see cref="BlockUserCommand"/>: <c>contacts.unblock#b550d328</c>.</summary>
    public sealed class UnblockUserCommand
    {
        public UserId Target { get; private set; }
        public long AccessHash { get; private set; }

        public UnblockUserCommand(UserId target, long accessHash)
        {
            if (target.Value <= 0) throw new ArgumentException("target must be positive", "target");
            Target = target;
            AccessHash = accessHash;
        }
    }
}
