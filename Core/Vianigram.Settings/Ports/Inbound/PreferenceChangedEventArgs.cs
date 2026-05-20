// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="ISettingsApi.PreferenceChanged"/>
    /// whenever a preference value transitions. Mirrors the
    /// <c>PreferenceChanged</c> domain event in a CLR-event shape so XAML / UI
    /// layers that don't take an <c>IEventBus</c> dependency can still
    /// subscribe.
    /// </summary>
    public sealed class PreferenceChangedEventArgs : EventArgs
    {
        public PreferenceKey Key { get; private set; }
        public object OldValue { get; private set; }
        public object NewValue { get; private set; }
        public DateTime At { get; private set; }

        public PreferenceChangedEventArgs(PreferenceKey key, object oldValue, object newValue, DateTime at)
        {
            if (key == null) throw new ArgumentNullException("key");
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
            At = at;
        }
    }
}
