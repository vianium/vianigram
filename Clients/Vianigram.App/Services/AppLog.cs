// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppLog.cs
// App-layer logging façade. Pages / VMs / Services request a component-tagged
// IComponentLogger via AppLog.For("Component"). When the composition root is
// built, the call routes through ILoggerFactory; before it (or in degraded
// mode) the call falls back to a tiny EarlyLog-backed shim so the same
// [HH:MM:SS.fff Component] message format is emitted regardless.

using Vianigram.Composition.Roots;
using Vianigram.Kernel.Logging;

namespace Vianigram.App.Services
{
    /// <summary>
    /// Tiny helper that hands out <see cref="IComponentLogger"/> instances to
    /// non-DI hosts (the App's pages, view-models, and static services). When
    /// <see cref="App.Composition"/> is wired this routes through the
    /// registered <see cref="ILoggerFactory"/>; otherwise it returns a logger
    /// backed by <see cref="EarlyLog"/> so log calls before composition still
    /// produce the standard format.
    /// </summary>
    public static class AppLog
    {
        public static IComponentLogger For(string componentName)
        {
            VianigramCompositionRoot composition = null;
            try
            {
                composition = App.Composition;
            }
            catch
            {
                composition = null;
            }

            if (composition != null)
            {
                ILoggerFactory factory;
                if (composition.TryResolve<ILoggerFactory>(out factory) && factory != null)
                {
                    return factory.ForComponent(componentName);
                }
            }
            return new EarlyComponentLogger(componentName);
        }

        // Bridge: routes IComponentLogger calls into EarlyLog.Write so we get
        // the same [HH:MM:SS.fff Component] format before the composition
        // root has registered ILoggerFactory.
        private sealed class EarlyComponentLogger : IComponentLogger
        {
            private readonly string _component;

            public EarlyComponentLogger(string component)
            {
                _component = string.IsNullOrEmpty(component) ? "?" : component;
            }

            public void Trace(string message) { EarlyLog.Write(_component, message); }
            public void Debug(string message) { EarlyLog.Write(_component, message); }
            public void Info(string message) { EarlyLog.Write(_component, message); }
            public void Warn(string message) { EarlyLog.Write(_component, message); }
            public void Error(string message) { EarlyLog.Write(_component, message); }
            public void Fatal(string message) { EarlyLog.Write(_component, message); }

            public void Info(string message, long elapsedMs)
            {
                var body = (message ?? string.Empty) + " elapsed=" + elapsedMs.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms";
                EarlyLog.Write(_component, body);
            }
        }
    }
}
