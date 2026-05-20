// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Network classification used to pick an auto-download policy. Telegram
    /// surfaces three independent slots (cellular / wifi / roaming) plus an
    /// implicit fallback when the OS connectivity profile cannot be resolved.
    /// </summary>
    public enum NetworkKind
    {
        Unknown = 0,
        WiFi = 1,
        Cellular = 2,
        Roaming = 3
    }
}
