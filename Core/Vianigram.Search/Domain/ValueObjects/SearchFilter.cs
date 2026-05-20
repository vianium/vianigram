// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Search.Domain.ValueObjects
{
    /// <summary>
    /// Server-side message filter applied to <c>messages.search</c> /
    /// <c>messages.searchGlobal</c>. Each member maps to a distinct TL
    /// <c>MessagesFilter</c> constructor — see
    /// <c>Infrastructure.TlEncoder.EncodeFilter</c> for the wire ids.
    ///
    /// Architecture doc <c>docs/managed-architecture/12-search.md §3</c> spec'd
    /// this as a discriminated VO; for V1 we materialize it as a flat enum
    /// because no filter member carries payload (Telegram's filters that take a
    /// hashtag — e.g. <c>inputMessagesFilterMyMentions</c> — are not exposed
    /// here yet, the prompt narrows to media + url + phone + gif).
    /// </summary>
    public enum SearchFilter
    {
        /// <summary><c>inputMessagesFilterEmpty#57e2f66c</c> — no server-side filter.</summary>
        All = 0,
        /// <summary><c>inputMessagesFilterPhotos#9609a51c</c></summary>
        Photos = 1,
        /// <summary><c>inputMessagesFilterVideo#9fc00e65</c></summary>
        Videos = 2,
        /// <summary><c>inputMessagesFilterDocument#9eddf188</c></summary>
        Documents = 3,
        /// <summary><c>inputMessagesFilterVoice#50f5c392</c></summary>
        Voice = 4,
        /// <summary><c>inputMessagesFilterMusic#3751b49e</c></summary>
        Music = 5,
        /// <summary><c>inputMessagesFilterUrl#7ef0dd87</c></summary>
        Url = 6,
        /// <summary><c>inputMessagesFilterGif#ffc86587</c></summary>
        GIF = 7,
        /// <summary><c>inputMessagesFilterPhoneCalls#80c99768</c></summary>
        Phone = 8
    }
}
