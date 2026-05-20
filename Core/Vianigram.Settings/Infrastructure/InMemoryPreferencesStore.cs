// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// In-memory store: every preference lives in process memory guarded by
    /// a private monitor.
    ///
    /// Sufficient for cold-start, tests, and UI consumption while the
    /// LocalSettings-backed adapter in <c>Vianigram.Storage</c> (or the App
    /// composition root) is built. Hot-swap point: replace the binding in
    /// <see cref="Vianigram.Settings.Composition.SettingsCompositionRoot"/>
    /// with the persistent adapter and the application layer is unchanged.
    ///
    /// Thread-safety: every operation takes a lock on a private gate object.
    /// All values are stored as canonical strings — typed marshalling is owned
    /// by the application layer.
    /// </summary>
    public sealed class InMemoryPreferencesStore : IPreferencesStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public Task<Result<string, SettingsError>> GetRawAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(key))
                return Task.FromResult(Result<string, SettingsError>.Fail(SettingsError.InvalidValue("key required")));

            lock (_gate)
            {
                string value;
                if (_values.TryGetValue(key, out value))
                {
                    return Task.FromResult(Result<string, SettingsError>.Ok(value ?? string.Empty));
                }
            }
            return Task.FromResult(Result<string, SettingsError>.Fail(SettingsError.NotFound(key)));
        }

        public Task<Result<Unit, SettingsError>> SetRawAsync(string key, string value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(key))
                return Task.FromResult(Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("key required")));

            lock (_gate)
            {
                _values[key] = value ?? string.Empty;
            }
            return Task.FromResult(Result<Unit, SettingsError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, SettingsError>> RemoveAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(key))
                return Task.FromResult(Result<Unit, SettingsError>.Fail(SettingsError.InvalidValue("key required")));

            lock (_gate)
            {
                _values.Remove(key);
            }
            return Task.FromResult(Result<Unit, SettingsError>.Ok(Unit.Value));
        }

        public Task<Result<IDictionary<string, string>, SettingsError>> GetAllAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Dictionary<string, string> copy;
            lock (_gate)
            {
                copy = new Dictionary<string, string>(_values.Count, StringComparer.Ordinal);
                foreach (var kv in _values) copy[kv.Key] = kv.Value;
            }
            return Task.FromResult(Result<IDictionary<string, string>, SettingsError>.Ok((IDictionary<string, string>)copy));
        }
    }
}
