// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// InputUserSelf.cs — Vianigram.Account.Ports.Outbound
// Marker DTO for inputUserSelf — TL constructor with no fields.

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Marker for the TL <c>inputUserSelf</c> constructor (#f7c1b13f). The
    /// type carries no fields; the rpc adapter recognises the singleton and
    /// emits the bare TL header.
    /// </summary>
    public sealed class InputUserSelf
    {
        public static readonly InputUserSelf Instance = new InputUserSelf();
        private InputUserSelf() { }
        public override string ToString() { return "inputUserSelf"; }
    }
}
