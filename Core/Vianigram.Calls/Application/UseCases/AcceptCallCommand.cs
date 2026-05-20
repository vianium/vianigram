// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Application.UseCases
{
    /// <summary>
    /// Accept an incoming <c>phoneCallRequested</c> already persisted to
    /// the local repository. Generates <c>g_b</c>, computes the shared key
    /// (<c>g_a^b mod p</c>), and issues <c>phone.acceptCall</c>.
    /// </summary>
    public sealed class AcceptCallCommand
    {
        public CallId CallId { get; private set; }

        public AcceptCallCommand(CallId callId)
        {
            CallId = callId;
        }
    }
}
