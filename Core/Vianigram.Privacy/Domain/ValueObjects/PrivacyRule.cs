// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Privacy.Domain.ValueObjects
{
    /// <summary>
    /// The audience to which a single allow/disallow clause applies.
    ///
    /// <para>Maps to the TL <c>privacyValue*</c> / <c>inputPrivacyValue*</c>
    /// constructors:</para>
    /// <list type="bullet">
    ///   <item><description><see cref="Everyone"/> — <c>privacyValueAllowAll#65427b82</c> / <c>privacyValueDisallowAll#8b73e763</c></description></item>
    ///   <item><description><see cref="Contacts"/> — <c>privacyValueAllowContacts#fffe1bac</c> / <c>privacyValueDisallowContacts#f888fa1a</c></description></item>
    ///   <item><description><see cref="CloseFriends"/> — <c>privacyValueAllowCloseFriends#f7e8d89b</c></description></item>
    ///   <item><description><see cref="Users"/> — <c>privacyValueAllowUsers#b8905fb2</c> / <c>privacyValueDisallowUsers#e4621141</c></description></item>
    ///   <item><description><see cref="Chats"/> — <c>privacyValueAllowChatParticipants#6b134e8e</c> / <c>privacyValueDisallowChatParticipants#41c87565</c></description></item>
    /// </list>
    /// </summary>
    public enum PrivacyAudience
    {
        Unknown = 0,
        Everyone = 1,
        Contacts = 2,
        CloseFriends = 3,
        Users = 4,
        Chats = 5
    }

    /// <summary>
    /// A single TL clause inside a <see cref="PrivacyRule"/>: either "allow" or
    /// "disallow" applied to one <see cref="PrivacyAudience"/>. Telegram
    /// composes the effective rule as an ordered list — the first matching
    /// clause wins. <see cref="PrivacyRule.Clauses"/> preserves that order.
    ///
    /// <para>Immutable. Constructed only via the <see cref="PrivacyClause"/>
    /// factories (<c>AllowAll()</c> / <c>DisallowUsers(ids)</c> / etc.).</para>
    /// </summary>
    public sealed class PrivacyClause
    {
        private static readonly long[] EmptyIds = new long[0];

        private readonly bool _allow;
        private readonly PrivacyAudience _audience;
        private readonly long[] _ids;

        private PrivacyClause(bool allow, PrivacyAudience audience, long[] ids)
        {
            _allow = allow;
            _audience = audience;
            _ids = ids ?? EmptyIds;
        }

        /// <summary>True for an "allow" clause; false for "disallow".</summary>
        public bool Allow { get { return _allow; } }
        public PrivacyAudience Audience { get { return _audience; } }

        /// <summary>
        /// Defensive copy of the user / chat ids in scope. Empty for
        /// <see cref="PrivacyAudience.Everyone"/>, <see cref="PrivacyAudience.Contacts"/>,
        /// and <see cref="PrivacyAudience.CloseFriends"/>.
        /// </summary>
        public IList<long> Ids
        {
            get
            {
                if (_ids.Length == 0) return new long[0];
                long[] copy = new long[_ids.Length];
                Array.Copy(_ids, copy, _ids.Length);
                return copy;
            }
        }

        // ---- Factories -------------------------------------------------------

        public static PrivacyClause AllowAll() { return new PrivacyClause(true, PrivacyAudience.Everyone, EmptyIds); }
        public static PrivacyClause DisallowAll() { return new PrivacyClause(false, PrivacyAudience.Everyone, EmptyIds); }
        public static PrivacyClause AllowContacts() { return new PrivacyClause(true, PrivacyAudience.Contacts, EmptyIds); }
        public static PrivacyClause DisallowContacts() { return new PrivacyClause(false, PrivacyAudience.Contacts, EmptyIds); }
        public static PrivacyClause AllowCloseFriends() { return new PrivacyClause(true, PrivacyAudience.CloseFriends, EmptyIds); }

        public static PrivacyClause AllowUsers(IList<long> ids) { return new PrivacyClause(true, PrivacyAudience.Users, ToArray(ids)); }
        public static PrivacyClause DisallowUsers(IList<long> ids) { return new PrivacyClause(false, PrivacyAudience.Users, ToArray(ids)); }
        public static PrivacyClause AllowChats(IList<long> ids) { return new PrivacyClause(true, PrivacyAudience.Chats, ToArray(ids)); }
        public static PrivacyClause DisallowChats(IList<long> ids) { return new PrivacyClause(false, PrivacyAudience.Chats, ToArray(ids)); }

        private static long[] ToArray(IList<long> source)
        {
            if (source == null || source.Count == 0) return EmptyIds;
            long[] copy = new long[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }

        public override string ToString()
        {
            string verb = _allow ? "Allow" : "Disallow";
            if (_ids.Length == 0) return verb + "(" + _audience + ")";
            return verb + "(" + _audience + ":" + _ids.Length + ")";
        }
    }

    /// <summary>
    /// An ordered list of <see cref="PrivacyClause"/>s for a single
    /// <see cref="PrivacyKey"/>. Telegram evaluates clauses top-to-bottom and
    /// the first matching clause wins; canonical UI shapes (e.g. "Contacts
    /// except @alice") map to a <c>[DisallowUsers([@alice]), AllowContacts]</c>
    /// pair.
    ///
    /// <para>Immutable. Constructed via <see cref="PrivacyRule.Of"/> or one of
    /// the convenience factories.</para>
    /// </summary>
    public sealed class PrivacyRule
    {
        private static readonly PrivacyClause[] EmptyClauses = new PrivacyClause[0];
        private readonly PrivacyClause[] _clauses;

        private PrivacyRule(PrivacyClause[] clauses)
        {
            _clauses = clauses ?? EmptyClauses;
        }

        public IList<PrivacyClause> Clauses
        {
            get
            {
                if (_clauses.Length == 0) return EmptyClauses;
                PrivacyClause[] copy = new PrivacyClause[_clauses.Length];
                Array.Copy(_clauses, copy, _clauses.Length);
                return copy;
            }
        }

        public int Count { get { return _clauses.Length; } }

        public static PrivacyRule Of(params PrivacyClause[] clauses)
        {
            if (clauses == null || clauses.Length == 0)
                return new PrivacyRule(EmptyClauses);

            // Validate: no nulls.
            for (int i = 0; i < clauses.Length; i++)
                if (clauses[i] == null) throw new ArgumentNullException("clauses[" + i + "]");

            PrivacyClause[] copy = new PrivacyClause[clauses.Length];
            Array.Copy(clauses, copy, clauses.Length);
            return new PrivacyRule(copy);
        }

        public static PrivacyRule Of(IList<PrivacyClause> clauses)
        {
            if (clauses == null || clauses.Count == 0)
                return new PrivacyRule(EmptyClauses);
            PrivacyClause[] arr = new PrivacyClause[clauses.Count];
            for (int i = 0; i < clauses.Count; i++)
            {
                if (clauses[i] == null) throw new ArgumentNullException("clauses[" + i + "]");
                arr[i] = clauses[i];
            }
            return new PrivacyRule(arr);
        }

        // ---- Convenience shortcuts for the canonical UI shapes ---------------

        public static PrivacyRule AllowEveryone() { return Of(PrivacyClause.AllowAll()); }
        public static PrivacyRule AllowContacts() { return Of(PrivacyClause.AllowContacts()); }
        public static PrivacyRule Nobody() { return Of(PrivacyClause.DisallowAll()); }

        public override string ToString()
        {
            if (_clauses.Length == 0) return "PrivacyRule(empty)";
            var parts = new System.Text.StringBuilder("PrivacyRule[");
            for (int i = 0; i < _clauses.Length; i++)
            {
                if (i > 0) parts.Append(", ");
                parts.Append(_clauses[i]);
            }
            parts.Append("]");
            return parts.ToString();
        }
    }
}
