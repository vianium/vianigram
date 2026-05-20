// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Messages.Ports.Outbound
{
    /// <summary>
    /// Allocates monotonic, negative client-temp ids used by optimistic sends.
    /// Each session starts from -1 and decrements; values reset on app launch
    /// (negative space never collides with server-assigned positive ids).
    /// </summary>
    public interface IMessageIdGenerator
    {
        long NextClientTempId();
    }
}
