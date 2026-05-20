// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Search.Domain.ValueObjects
{
    /// <summary>
    /// Immutable description of a single search request. Carries the normalized
    /// query text, the optional peer scope (when present, the search is local
    /// to one chat — <c>messages.search</c>; when absent, it's
    /// <c>messages.searchGlobal</c>), the filter, and optional sender / date
    /// constraints.
    ///
    /// <para><b>Validation</b>: the application layer (handlers) rejects empty
    /// queries with <see cref="Domain.SearchError.QueryTooShort"/>; this VO
    /// itself only normalizes whitespace and stores. The minimum length is 1
    /// for global search and 2 for per-chat search per Telegram server
    /// behavior, but per-context handlers enforce the threshold so the VO can
    /// be reused for both.</para>
    /// </summary>
    public sealed class SearchQuery
    {
        /// <summary>Trimmed user-typed query text.</summary>
        public string Text { get; private set; }

        /// <summary>
        /// Opaque peer key (e.g. <c>"user:42"</c>, <c>"chat:7"</c>,
        /// <c>"channel:1001"</c>). Null = global search across every dialog.
        /// </summary>
        public string PeerKey { get; private set; }

        /// <summary>Server-side message filter, defaults to <see cref="SearchFilter.All"/>.</summary>
        public SearchFilter Filter { get; private set; }

        /// <summary>
        /// Restrict results to messages sent by this peer key (e.g. inside a
        /// group chat). Null disables the constraint. Only used by per-chat
        /// search in V1; <c>messages.searchGlobal</c> ignores it.
        /// </summary>
        public string FromUser { get; private set; }

        /// <summary>Lower bound on message date (inclusive). Null = no bound.</summary>
        public DateTime? MinDate { get; private set; }

        /// <summary>Upper bound on message date (inclusive). Null = no bound.</summary>
        public DateTime? MaxDate { get; private set; }

        public SearchQuery(
            string text,
            string peerKey,
            SearchFilter filter,
            string fromUser,
            DateTime? minDate,
            DateTime? maxDate)
        {
            Text = Normalize(text);
            PeerKey = string.IsNullOrEmpty(peerKey) ? null : peerKey;
            Filter = filter;
            FromUser = string.IsNullOrEmpty(fromUser) ? null : fromUser;
            MinDate = minDate;
            MaxDate = maxDate;
        }

        public SearchQuery(string text, string peerKey, SearchFilter filter)
            : this(text, peerKey, filter, null, null, null) { }

        public SearchQuery(string text, SearchFilter filter)
            : this(text, null, filter, null, null, null) { }

        /// <summary>True when the query targets a single peer (per-chat search).</summary>
        public bool IsScopedToPeer { get { return PeerKey != null; } }

        /// <summary>Trim and collapse internal whitespace, keep null-safe.</summary>
        private static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) return string.Empty;
            // Collapse runs of whitespace into a single space.
            var sb = new System.Text.StringBuilder(trimmed.Length);
            bool prevSpace = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (!prevSpace) sb.Append(' ');
                    prevSpace = true;
                }
                else
                {
                    sb.Append(c);
                    prevSpace = false;
                }
            }
            return sb.ToString();
        }

        public override string ToString()
        {
            string scope = PeerKey == null ? "global" : ("peer=" + PeerKey);
            return "SearchQuery(" + scope + " text='" + Text + "' filter=" + Filter + ")";
        }
    }
}
