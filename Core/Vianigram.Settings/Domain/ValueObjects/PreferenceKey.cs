// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Strongly-typed identifier for a user preference. Pairs a stable string
    /// name (e.g. <c>"appearance.theme_mode"</c>, <c>"data.autoDownload.photos.wifi"</c>)
    /// with the CLR <see cref="Type"/> of its value. The application layer uses
    /// the string name as the storage key; the type info lets the typed
    /// <c>Get/Set</c> overloads enforce shape at the boundary.
    ///
    /// Immutable. Equality is by ordinal name comparison — a key with the same
    /// name but different type is treated as the same key (the type info is a
    /// tag for the API surface, not part of identity, so storage migrations
    /// that change a key's value type don't leak two parallel slots).
    ///
    /// V1 keeps the non-generic shape here. The architecture doc proposes a
    /// generic <c>PreferenceKey&lt;T&gt;</c> with embedded default + validator;
    /// we model that as <c>(PreferenceKey, default-from-PreferenceKeys, ...)</c>
    /// in the application layer to keep the value object reference-comparable
    /// across handler boundaries.
    /// </summary>
    public sealed class PreferenceKey : IEquatable<PreferenceKey>
    {
        private readonly string _name;
        private readonly Type _valueType;

        public PreferenceKey(string name, Type valueType)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", "name");
            if (valueType == null) throw new ArgumentNullException("valueType");
            _name = name;
            _valueType = valueType;
        }

        public string Name { get { return _name; } }
        public Type ValueType { get { return _valueType; } }

        /// <summary>Convenience: build a key from a CLR type parameter.</summary>
        public static PreferenceKey Of<T>(string name)
        {
            return new PreferenceKey(name, typeof(T));
        }

        public bool Equals(PreferenceKey other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return string.Equals(_name, other._name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PreferenceKey);
        }

        public override int GetHashCode()
        {
            return _name.GetHashCode();
        }

        public override string ToString()
        {
            return _name + " : " + _valueType.Name;
        }

        public static bool operator ==(PreferenceKey a, PreferenceKey b)
        {
            if (ReferenceEquals(a, b)) return true;
            if ((object)a == null || (object)b == null) return false;
            return a.Equals(b);
        }

        public static bool operator !=(PreferenceKey a, PreferenceKey b)
        {
            return !(a == b);
        }
    }
}
