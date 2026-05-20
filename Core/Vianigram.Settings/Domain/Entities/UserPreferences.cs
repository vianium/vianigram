// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Settings.Domain.Events;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Domain.Entities
{
    /// <summary>
    /// Aggregate root for the Settings context. Holds a typed key→value map
    /// addressing every preference the application surfaces. Mutators stage
    /// <see cref="IDomainEvent"/> instances on a pending list so the handler /
    /// repository can drain them after the persistence write succeeds — same
    /// pattern used in <c>Vianigram.Notifications.Domain.Entities.NotificationProfile</c>.
    ///
    /// Identity equality on the inner dictionary is by <see cref="PreferenceKey.Name"/>
    /// (ordinal). The application layer treats the aggregate as a singleton
    /// per active account; multi-account scoping (<c>PreferenceScope.Account</c>)
    /// is left for V2 — the architecture doc tracks it under "open questions".
    /// </summary>
    public sealed class UserPreferences
    {
        private readonly Dictionary<string, object> _values;
        private readonly List<IDomainEvent> _pending;

        public UserPreferences()
        {
            _values = new Dictionary<string, object>(StringComparer.Ordinal);
            _pending = new List<IDomainEvent>(8);
        }

        /// <summary>Number of keys explicitly stored on the aggregate.</summary>
        public int Count { get { return _values.Count; } }

        /// <summary>True when the user has set any preference.</summary>
        public bool IsEmpty { get { return _values.Count == 0; } }

        /// <summary>
        /// Returns the stored value for <paramref name="key"/>, or
        /// <paramref name="fallback"/> when no value has been stored. Throws
        /// <see cref="InvalidCastException"/> when the stored value cannot be
        /// coerced to <typeparamref name="T"/> — application handlers map this
        /// to <c>SettingsError.TypeMismatch</c>.
        /// </summary>
        public T GetOrDefault<T>(PreferenceKey key, T fallback)
        {
            if (key == null) throw new ArgumentNullException("key");
            object boxed;
            if (!_values.TryGetValue(key.Name, out boxed)) return fallback;
            if (boxed == null) return fallback;
            if (boxed is T) return (T)boxed;
            throw new InvalidCastException("preference '" + key.Name + "' stored as " + boxed.GetType().Name + " but read as " + typeof(T).Name);
        }

        /// <summary>True when the supplied key has an explicit value.</summary>
        public bool Contains(PreferenceKey key)
        {
            if (key == null) return false;
            return _values.ContainsKey(key.Name);
        }

        /// <summary>
        /// Set a value for <paramref name="key"/>. Stages a
        /// <see cref="PreferenceChanged"/> event when the value differs from
        /// the previous. Specialized events
        /// (<see cref="ThemeChanged"/>, <see cref="LanguageChanged"/>,
        /// <see cref="DataPolicyChanged"/>) are NOT staged here — the handler
        /// layer issues those after re-reading the relevant slice, so we keep
        /// the aggregate generic.
        /// </summary>
        public void Set<T>(PreferenceKey key, T value, DateTime at)
        {
            if (key == null) throw new ArgumentNullException("key");
            object previous;
            _values.TryGetValue(key.Name, out previous);
            object boxed = (object)value;
            if (Equals(previous, boxed)) return;
            _values[key.Name] = boxed;
            Stage(new PreferenceChanged(key, previous, boxed, at));
        }

        /// <summary>
        /// Remove the explicit value for <paramref name="key"/>. Stages a
        /// <see cref="PreferenceChanged"/> with <c>NewValue=null</c> when a
        /// value was actually present.
        /// </summary>
        public void Remove(PreferenceKey key, DateTime at)
        {
            if (key == null) throw new ArgumentNullException("key");
            object previous;
            if (!_values.TryGetValue(key.Name, out previous)) return;
            _values.Remove(key.Name);
            Stage(new PreferenceChanged(key, previous, null, at));
        }

        /// <summary>
        /// Drop every stored value. Stages a single <see cref="PreferencesReset"/>
        /// carrying the affected count; per-key
        /// <see cref="PreferenceChanged"/> events are NOT individually staged
        /// to avoid event storms.
        /// </summary>
        public void Reset(DateTime at)
        {
            int count = _values.Count;
            if (count == 0) return;
            _values.Clear();
            Stage(new PreferencesReset(count, at));
        }

        /// <summary>
        /// Stage a <see cref="ThemeChanged"/> event. Called by the handler
        /// after the underlying preference write succeeds.
        /// </summary>
        public void RecordThemeChanged(Theme oldTheme, Theme newTheme, DateTime at)
        {
            if (oldTheme == newTheme) return;
            Stage(new ThemeChanged(oldTheme, newTheme, at));
        }

        /// <summary>
        /// Stage a <see cref="LanguageChanged"/> event. Called by the handler
        /// after the underlying preference write succeeds.
        /// </summary>
        public void RecordLanguageChanged(LanguagePack oldPack, LanguagePack newPack, DateTime at)
        {
            if (newPack == null) throw new ArgumentNullException("newPack");
            if (oldPack != null && oldPack.Equals(newPack)) return;
            Stage(new LanguageChanged(oldPack, newPack, at));
        }

        /// <summary>
        /// Stage a <see cref="DataPolicyChanged"/> event. Called by the handler
        /// after the underlying preference write succeeds.
        /// </summary>
        public void RecordDataPolicyChanged(NetworkKind network, DataUsagePolicy oldPolicy, DataUsagePolicy newPolicy, DateTime at)
        {
            if (newPolicy == null) throw new ArgumentNullException("newPolicy");
            if (oldPolicy != null && oldPolicy.Equals(newPolicy)) return;
            Stage(new DataPolicyChanged(network, oldPolicy, newPolicy, at));
        }

        /// <summary>
        /// Snapshot every stored key/value as an immutable copy. Used for
        /// import/export and for the storage adapter to serialize the aggregate.
        /// </summary>
        public IDictionary<string, object> Snapshot()
        {
            var copy = new Dictionary<string, object>(_values.Count, StringComparer.Ordinal);
            foreach (var kv in _values) copy[kv.Key] = kv.Value;
            return copy;
        }

        /// <summary>
        /// Replace the entire stored map (used by the repository hydration
        /// path). Does NOT stage events — the aggregate is being rebuilt, not
        /// mutated.
        /// </summary>
        public void HydrateFromSnapshot(IDictionary<string, object> snapshot)
        {
            _values.Clear();
            if (snapshot == null) return;
            foreach (var kv in snapshot)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                _values[kv.Key] = kv.Value;
            }
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
    }
}
