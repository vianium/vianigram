// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Domain.Events
{
    /// <summary>
    /// Emitted whenever a preference value transitions on the aggregate. The
    /// raw <see cref="OldValue"/> / <see cref="NewValue"/> are boxed to keep the
    /// event type non-generic — the bus dispatches by static type, so a generic
    /// <c>PreferenceChanged&lt;T&gt;</c> would force every consumer to know T.
    /// Callers cast on <see cref="Key"/>'s declared <see cref="PreferenceKey.ValueType"/>.
    /// </summary>
    public sealed class PreferenceChanged : IDomainEvent
    {
        public PreferenceKey Key { get; private set; }
        public object OldValue { get; private set; }
        public object NewValue { get; private set; }
        public DateTime At { get; private set; }

        public PreferenceChanged(PreferenceKey key, object oldValue, object newValue, DateTime at)
        {
            if (key == null) throw new ArgumentNullException("key");
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the active <see cref="Theme"/> changes. Carries both the
    /// previous and new value so subscribers (e.g. <c>Vianigram.App</c>) can
    /// transition resources without re-querying the aggregate.
    /// </summary>
    public sealed class ThemeChanged : IDomainEvent
    {
        public Theme OldTheme { get; private set; }
        public Theme NewTheme { get; private set; }
        public DateTime At { get; private set; }

        public ThemeChanged(Theme oldTheme, Theme newTheme, DateTime at)
        {
            OldTheme = oldTheme;
            NewTheme = newTheme;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the active <see cref="LanguagePack"/> changes. Subscribers
    /// (e.g. the future <c>Vianigram.I18n</c>) reload the strings table.
    /// </summary>
    public sealed class LanguageChanged : IDomainEvent
    {
        public LanguagePack OldPack { get; private set; }
        public LanguagePack NewPack { get; private set; }
        public DateTime At { get; private set; }

        public LanguageChanged(LanguagePack oldPack, LanguagePack newPack, DateTime at)
        {
            if (newPack == null) throw new ArgumentNullException("newPack");
            OldPack = oldPack;
            NewPack = newPack;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a per-network <see cref="DataUsagePolicy"/> changes.
    /// Subscribers (media downloader, sync) reroute auto-download decisions.
    /// </summary>
    public sealed class DataPolicyChanged : IDomainEvent
    {
        public NetworkKind Network { get; private set; }
        public DataUsagePolicy OldPolicy { get; private set; }
        public DataUsagePolicy NewPolicy { get; private set; }
        public DateTime At { get; private set; }

        public DataPolicyChanged(NetworkKind network, DataUsagePolicy oldPolicy, DataUsagePolicy newPolicy, DateTime at)
        {
            if (newPolicy == null) throw new ArgumentNullException("newPolicy");
            Network = network;
            OldPolicy = oldPolicy;
            NewPolicy = newPolicy;
            At = at;
        }
    }

    /// <summary>
    /// Emitted after a <c>ResetToDefaults</c> succeeds. Carries the count of
    /// keys that actually changed (i.e. were not already at default).
    /// </summary>
    public sealed class PreferencesReset : IDomainEvent
    {
        public int KeysAffected { get; private set; }
        public DateTime At { get; private set; }

        public PreferencesReset(int keysAffected, DateTime at)
        {
            KeysAffected = keysAffected;
            At = at;
        }
    }
}
