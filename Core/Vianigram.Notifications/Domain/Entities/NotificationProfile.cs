// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Notifications.Domain.Events;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Domain.Entities
{
    /// <summary>
    /// Aggregate root holding the active account's notification settings.
    ///
    /// State:
    ///   * <c>global</c>      — the user's default <see cref="MuteRule"/>
    ///                          (sentinel <see cref="MuteRule.Global"/>).
    ///   * <c>perPeer</c>     — overrides keyed by peer (e.g. "user:42",
    ///                          "chat:-100123"). A peer with no entry inherits
    ///                          <c>global</c>.
    ///   * <c>badge</c>       — last computed <see cref="BadgeCount"/>.
    ///   * <c>lastSyncAt</c>  — when <c>account.getNotifySettings</c> last
    ///                          succeeded.
    ///
    /// Mutators stage <see cref="IDomainEvent"/> instances on a pending list so
    /// the handler / repository can drain them after the persistence write
    /// succeeds. Same pattern used in <see cref="Vianigram.Stickers"/>.
    /// </summary>
    public sealed class NotificationProfile
    {
        private MuteRule _global;
        private readonly Dictionary<string, MuteRule> _perPeer;
        private BadgeCount _badge;
        private readonly List<IDomainEvent> _pending;
        private DateTime _lastSyncAt;

        public NotificationProfile()
        {
            _global = MuteRule.DefaultFor(MuteRule.Global);
            _perPeer = new Dictionary<string, MuteRule>(StringComparer.Ordinal);
            _badge = BadgeCount.Empty;
            _pending = new List<IDomainEvent>(8);
            _lastSyncAt = DateTime.MinValue;
        }

        public MuteRule Global { get { return _global; } }
        public BadgeCount Badge { get { return _badge; } }
        public DateTime LastSyncAt { get { return _lastSyncAt; } }
        public int PerPeerCount { get { return _perPeer.Count; } }

        /// <summary>
        /// Resolve the effective rule for <paramref name="peerKey"/>: an
        /// explicit override if present, otherwise <see cref="Global"/>.
        /// Never returns null.
        /// </summary>
        public MuteRule Resolve(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return _global;
            MuteRule rule;
            if (_perPeer.TryGetValue(peerKey, out rule) && rule != null) return rule;
            return _global;
        }

        public bool HasOverride(string peerKey)
        {
            return !string.IsNullOrEmpty(peerKey) && _perPeer.ContainsKey(peerKey);
        }

        public IList<MuteRule> OverridesSnapshot()
        {
            var list = new List<MuteRule>(_perPeer.Count);
            foreach (var kv in _perPeer)
            {
                if (kv.Value != null) list.Add(kv.Value);
            }
            return list;
        }

        /// <summary>
        /// Replace the rule for <paramref name="peerKey"/> (use
        /// <see cref="MuteRule.Global"/> for the default rule). Stages a
        /// <see cref="MuteRuleChanged"/> domain event when the rule actually
        /// changes.
        /// </summary>
        public void SetMute(string peerKey, MuteRule rule, DateTime at)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            string key = string.IsNullOrEmpty(peerKey) ? MuteRule.Global : peerKey;

            MuteRule existing = Resolve(key);
            bool changed = !RulesEqual(existing, rule);

            if (string.Equals(key, MuteRule.Global, StringComparison.Ordinal))
            {
                _global = rule;
            }
            else
            {
                _perPeer[key] = rule;
            }

            if (changed)
            {
                Stage(new MuteRuleChanged(key, rule, at));
            }
        }

        /// <summary>
        /// Mute every known peer plus global. Used by the "Mute All" command.
        /// Stages one <see cref="MuteRuleChanged"/> per affected key.
        /// </summary>
        public void MuteAll(DateTime? muteUntilUtc, DateTime at)
        {
            DateTime until = muteUntilUtc ?? DateTime.MaxValue;
            SetMute(MuteRule.Global, _global.With(muteUntil: until), at);
            // Snapshot keys so we can mutate the dict during iteration.
            var keys = new List<string>(_perPeer.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                MuteRule existing = _perPeer[keys[i]];
                SetMute(keys[i], existing.With(muteUntil: until), at);
            }
        }

        /// <summary>
        /// Apply the per-peer settings returned by
        /// <c>account.getNotifySettings</c>. Each entry is upserted; entries
        /// not present in the response are left intact (Telegram only returns
        /// non-default exceptions).
        /// </summary>
        public void ApplyServerSync(MuteRule global, IList<MuteRule> exceptions, DateTime at)
        {
            if (global != null)
            {
                SetMute(MuteRule.Global, global, at);
            }
            if (exceptions != null)
            {
                for (int i = 0; i < exceptions.Count; i++)
                {
                    MuteRule rule = exceptions[i];
                    if (rule == null || string.IsNullOrEmpty(rule.PeerKey)) continue;
                    SetMute(rule.PeerKey, rule, at);
                }
            }
            _lastSyncAt = at;
        }

        /// <summary>
        /// Set the current badge count. Stages a <see cref="BadgeUpdated"/>
        /// event iff the value actually changed.
        /// </summary>
        public void SetBadge(BadgeCount count, DateTime at)
        {
            if (_badge.Equals(count)) return;
            _badge = count;
            Stage(new BadgeUpdated(count, at));
        }

        /// <summary>
        /// Clear all unread state for a peer (used by MarkAsRead). Today this
        /// just resets the badge contribution; per-peer counts are tracked by
        /// the Messaging context, not here. We expose the hook so the handler
        /// can stage the badge update event consistently.
        /// </summary>
        public void ClearForPeer(string peerKey, BadgeCount newTotal, DateTime at)
        {
            if (string.IsNullOrEmpty(peerKey)) return;
            SetBadge(newTotal, at);
        }

        /// <summary>Record that a notification was queued (stages the event).</summary>
        public void RecordQueued(NotificationKind kind, string peerKey, string body, DateTime at)
        {
            Stage(new IncomingNotificationQueued(kind, peerKey ?? string.Empty, body ?? string.Empty, at));
        }

        /// <summary>Record that a queued notification was delivered to the platform.</summary>
        public void RecordDelivered(NotificationKind kind, string peerKey, DateTime at)
        {
            Stage(new NotificationDelivered(kind, peerKey ?? string.Empty, at));
        }

        /// <summary>Drain pending domain events for the handler to publish post-persistence.</summary>
        public IList<IDomainEvent> DequeuePendingEvents()
        {
            if (_pending.Count == 0) return new IDomainEvent[0];
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        private void Stage(IDomainEvent evt)
        {
            _pending.Add(evt);
        }

        private static bool RulesEqual(MuteRule a, MuteRule b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return string.Equals(a.PeerKey, b.PeerKey, StringComparison.Ordinal)
                && Nullable.Equals(a.MuteUntil, b.MuteUntil)
                && string.Equals(a.Sound, b.Sound, StringComparison.Ordinal)
                && a.ShowPreviews == b.ShowPreviews;
        }
    }
}
