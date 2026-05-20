// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ILoggerFactory.cs — Vianigram.Kernel.Logging
// Factory port returning component-scoped IComponentLogger instances.

namespace Vianigram.Kernel.Logging
{
    /// <summary>
    /// Composition-root-resolved factory for <see cref="IComponentLogger"/>.
    /// Bounded contexts ctor-inject this and call <see cref="ForComponent"/>
    /// once per class to obtain a logger pre-tagged with its component name.
    /// </summary>
    public interface ILoggerFactory
    {
        IComponentLogger ForComponent(string componentName);
    }
}
