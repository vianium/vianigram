// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Search.Domain.ValueObjects
{
    /// <summary>
    /// Discriminator for the entity carried by a <see cref="SearchHit"/>.
    /// Mirrors the four payload shapes Telegram surfaces across the search
    /// RPCs: a user (<c>contacts.found.users</c>), a chat / channel
    /// (<c>contacts.found.chats</c>), a message (<c>messages.messages</c>),
    /// and a "document" hit (an attachment-only search result — V1 reuses
    /// <see cref="Message"/> for documents and reserves the discriminator for
    /// future direct-document indices).
    /// </summary>
    public enum SearchHitKind
    {
        Unknown = 0,
        User = 1,
        Chat = 2,
        Channel = 3,
        Message = 4,
        Document = 5
    }

    /// <summary>
    /// Single search result. Immutable.
    ///
    /// <para><b>Payload shape</b>: deliberately untyped (<see cref="object"/>)
    /// to keep the bounded context decoupled from <c>Vianigram.Messages</c> /
    /// <c>Vianigram.Contacts</c> domain types. The composition root maps the
    /// <c>Payload</c> to the consumer's domain shape via an ACL adapter.
    /// V1's <c>TlDecoder</c> populates <c>Payload</c> with simple POCO record
    /// types nested in <see cref="Infrastructure.TlDecoder"/>.</para>
    ///
    /// <para><b>Score</b>: Telegram does not return a numeric relevance, so
    /// V1 uses positional rank (descending) — first hit on a page = page-size,
    /// last = 1. Subscribers that consume the bus event can ignore it.</para>
    /// </summary>
    public sealed class SearchHit
    {
        public SearchHitKind ResultType { get; private set; }
        public object Payload { get; private set; }
        public double Score { get; private set; }

        public SearchHit(SearchHitKind resultType, object payload, double score)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            ResultType = resultType;
            Payload = payload;
            Score = score;
        }

        public override string ToString()
        {
            return "SearchHit(" + ResultType + " score=" + Score.ToString("F2") + ")";
        }
    }
}
