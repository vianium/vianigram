// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SelfProfile.cs — Vianigram.Account.Domain.ValueObjects
// Read-only projection of the current user's profile (users.getFullUser response).

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Read-only snapshot of the signed-in user's profile fields. Constructed
    /// from a <c>users.getFullUser</c> response. Strings default to empty,
    /// never <c>null</c>, so consumers can bind directly to UI without
    /// null-checks.
    /// </summary>
    public sealed class SelfProfile
    {
        public long UserId { get; private set; }
        public string FirstName { get; private set; }
        public string LastName { get; private set; }
        public string Username { get; private set; }
        public string Phone { get; private set; }
        public string Bio { get; private set; }

        public SelfProfile(
            long userId,
            string firstName,
            string lastName,
            string username,
            string phone,
            string bio)
        {
            UserId = userId;
            FirstName = firstName ?? string.Empty;
            LastName = lastName ?? string.Empty;
            Username = username ?? string.Empty;
            Phone = phone ?? string.Empty;
            Bio = bio ?? string.Empty;
        }

        public override string ToString()
        {
            return "self_profile(user_id=" + UserId + ", username=@" + Username + ")";
        }
    }
}
