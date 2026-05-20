// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Search.Application.UseCases
{
    /// <summary>
    /// Issue <c>contacts.search#11f812d8</c>.
    ///
    /// <para><b>Note</b>: the same RPC is also surfaced by the Contacts
    /// bounded context. Search owns it for the global "@username" discovery
    /// surface — different consumer, different bounded-context concern. The
    /// underlying adapter is the same; the per-context port (<see cref="Ports.Outbound.IMtProtoRpcPort"/>)
    /// keeps both contexts decoupled at the type level.</para>
    /// </summary>
    public sealed class SearchUsersCommand
    {
        public string Query { get; private set; }
        public int Limit { get; private set; }

        public SearchUsersCommand(string query, int limit = 20)
        {
            Query = query ?? string.Empty;
            Limit = limit <= 0 ? 20 : limit;
        }
    }
}
