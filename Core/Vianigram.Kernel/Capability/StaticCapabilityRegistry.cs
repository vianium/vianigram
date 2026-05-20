// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Kernel.Capability
{
    /// <summary>
    /// Immutable <see cref="ICapabilityRegistry"/> backed by a <see cref="HashSet{T}"/>.
    /// Configure once at composition time using <see cref="Builder"/>.
    /// </summary>
    public sealed class StaticCapabilityRegistry : ICapabilityRegistry
    {
        private readonly HashSet<string> _enabled;

        private StaticCapabilityRegistry(HashSet<string> enabled)
        {
            _enabled = enabled;
        }

        public bool IsEnabled(CapabilityId id)
        {
            if (id == null) return false;
            return _enabled.Contains(id.Name);
        }

        public static Builder Create()
        {
            return new Builder();
        }

        /// <summary>
        /// Mutable accumulator used during composition. Call <see cref="Build"/> to
        /// freeze into an immutable registry.
        /// </summary>
        public sealed class Builder
        {
            private readonly HashSet<string> _enabled = new HashSet<string>(StringComparer.Ordinal);

            public Builder Enable(CapabilityId id)
            {
                if (id == null) throw new ArgumentNullException("id");
                _enabled.Add(id.Name);
                return this;
            }

            public Builder Enable(string name)
            {
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", "name");
                _enabled.Add(name);
                return this;
            }

            public StaticCapabilityRegistry Build()
            {
                return new StaticCapabilityRegistry(new HashSet<string>(_enabled, StringComparer.Ordinal));
            }
        }
    }
}
