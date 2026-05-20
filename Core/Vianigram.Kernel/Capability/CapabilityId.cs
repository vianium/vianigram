// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Kernel.Capability
{
    /// <summary>
    /// Strong-typed capability flag identifier. Wrap a string to prevent
    /// stringly-typed comparisons throughout the code base.
    /// </summary>
    public sealed class CapabilityId : IEquatable<CapabilityId>
    {
        public string Name { get; private set; }

        public CapabilityId(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name required", "name");
            Name = name;
        }

        public bool Equals(CapabilityId other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return string.Equals(Name, other.Name, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as CapabilityId);
        }

        public override int GetHashCode()
        {
            return Name == null ? 0 : Name.GetHashCode();
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
